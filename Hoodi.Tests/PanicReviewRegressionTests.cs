using System.Net;
using System.Net.Http.Json;
using Hoodi.Application.Panico;
using Hoodi.Infrastructure.Panico;
using Hoodi.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hoodi.Tests;

public sealed class PanicReviewRegressionTests
{
    [Fact]
    public async Task OwnerDeletion_MustNotRestoreRevokedRecipientsAccess()
    {
        using var factory = new AuthFactory();
        using var owner = factory.Client(); using var revoked = factory.Client(); using var active = factory.Client();
        var a = await PanicoAndContaIntegrationTests.Seed(factory, owner, "owner");
        var b = await PanicoAndContaIntegrationTests.Seed(factory, revoked, "revoked");
        var c = await PanicoAndContaIntegrationTests.Seed(factory, active, "active");
        var revokedLink = await PanicoAndContaIntegrationTests.Link(factory, a, b);
        await PanicoAndContaIntegrationTests.Link(factory, a, c);
        var request = await PanicoAndContaIntegrationTests.Create(owner, Guid.NewGuid());
        var detailUrl = $"/v1/panico/{request.PedidoId}";
        Assert.Equal(HttpStatusCode.OK, (await revoked.GetAsync(detailUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await owner.DeleteAsync($"/v1/contatos/{revokedLink}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await revoked.GetAsync(detailUrl)).StatusCode);
        Assert.Empty((await revoked.GetFromJsonAsync<PanicoRecebidoResumo[]>("/v1/panico/recebidos"))!);

        Assert.Equal(HttpStatusCode.NoContent, (await Delete(owner)).StatusCode);

        Assert.Equal(HttpStatusCode.NotFound, (await revoked.GetAsync(detailUrl)).StatusCode);
        Assert.Empty((await revoked.GetFromJsonAsync<PanicoRecebidoResumo[]>("/v1/panico/recebidos"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await revoked.PostAsync(detailUrl + "/abertura", null)).StatusCode);
        var history = (await active.GetFromJsonAsync<PanicoDetalhe>(detailUrl))!;
        Assert.Equal("Conta excluída", history.TitularNome);
        Assert.Null(history.Latitude); Assert.Null(history.Longitude); Assert.Null(history.Observacao);
        Assert.False(history.LocalizacaoDisponivel);
        var received = Assert.Single((await active.GetFromJsonAsync<PanicoRecebidoResumo[]>("/v1/panico/recebidos"))!);
        Assert.Equal(request.PedidoId, received.PedidoId); Assert.Equal("Conta excluída", received.TitularNome);
        Assert.Equal(HttpStatusCode.NoContent, (await active.PostAsync(detailUrl + "/abertura", null)).StatusCode);
    }

    [Theory]
    [InlineData(24, 72)]
    [InlineData(72, 48)]
    public async Task Retention_MustUseRoleAndPurgeOnlyAfterBothDeadlines(int contactHours, int ownerHours)
    {
        using var factory = new AuthFactory(); using var owner = factory.Client(); using var contact = factory.Client();
        var a = await PanicoAndContaIntegrationTests.Seed(factory, owner, "owner");
        var b = await PanicoAndContaIntegrationTests.Seed(factory, contact, "contact");
        await PanicoAndContaIntegrationTests.Link(factory, a, b);
        using var scope = factory.Services.CreateScope(); var options = scope.ServiceProvider.GetRequiredService<IOptions<PanicoOptions>>().Value;
        options.RetencaoLocalizacaoHoras = contactHours; options.RetencaoTitularHoras = ownerHours;
        var service = scope.ServiceProvider.GetRequiredService<IPanicoService>();
        var key = Guid.NewGuid(); var result = await service.RegistrarAsync(a, new(key, "app", 1, 2, "SYNTHETIC_NOTE"), default);
        var worker = scope.ServiceProvider.GetRequiredService<PanicoDispatchProcessor>();
        factory.Clock.Advance(TimeSpan.FromHours(Math.Min(contactHours, ownerHours)));
        await worker.ProcessAsync(false, default);
        var own = await service.DetalheAsync(a, result.PedidoId, default); var received = await service.DetalheAsync(b, result.PedidoId, default);
        Assert.Equal(ownerHours > contactHours, own.LocalizacaoDisponivel);
        Assert.Equal(contactHours > ownerHours, received.LocalizacaoDisponivel);
        var db = scope.ServiceProvider.GetRequiredService<HoodiDbContext>(); db.ChangeTracker.Clear();
        Assert.NotNull((await db.PedidosPanico.SingleAsync()).DadosCifrados);
        factory.Clock.Advance(TimeSpan.FromHours(Math.Abs(contactHours - ownerHours)));
        Assert.False((await service.DetalheAsync(a, result.PedidoId, default)).LocalizacaoDisponivel);
        Assert.False((await service.DetalheAsync(b, result.PedidoId, default)).LocalizacaoDisponivel);
        await worker.ProcessAsync(false, default); db.ChangeTracker.Clear();
        Assert.Null((await db.PedidosPanico.SingleAsync()).DadosCifrados);
        var replay = await service.RegistrarAsync(a, new(key, "watch", 3, 4), default);
        Assert.True(replay.Reutilizado); Assert.Equal(result.PedidoId, replay.PedidoId);
    }

    [Fact]
    public async Task DeletingRecipient_MustRemoveAccessToAnonymousHistory_AndIgnoreUnreadableMail()
    {
        using var factory = new AuthFactory(); using var owner = factory.Client(); using var contact = factory.Client();
        var a = await PanicoAndContaIntegrationTests.Seed(factory, owner, "owner");
        var b = await PanicoAndContaIntegrationTests.Seed(factory, contact, "contact");
        await PanicoAndContaIntegrationTests.Link(factory, a, b);
        var created = await owner.PostAsJsonAsync("/v1/panico", new { idempotencyKey = Guid.NewGuid(), origem = "app", latitude = 1, longitude = 2, observacao = "SYNTHETIC_PRIVATE_NOTE" });
        Assert.Equal(HttpStatusCode.Accepted, created.StatusCode);
        var result = (await created.Content.ReadFromJsonAsync<PanicoRegistrado>())!;
        var mailId = Guid.NewGuid();
        using (var setup = factory.Services.CreateScope())
        {
            var db = setup.ServiceProvider.GetRequiredService<HoodiDbContext>();
            db.Emails.Add(new EmailPendente { Id = mailId, ConteudoCifrado = "SYNTHETIC_UNREADABLE_CIPHERTEXT", CriadoEmUtc = factory.Clock.GetUtcNow() });
            await db.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.NoContent, (await Delete(owner)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await contact.GetAsync($"/v1/panico/{result.PedidoId}")).StatusCode);
        var anonymous = (await contact.GetFromJsonAsync<PanicoDetalhe>($"/v1/panico/{result.PedidoId}"))!;
        Assert.Equal("Conta excluída", anonymous.TitularNome); Assert.Null(anonymous.Latitude); Assert.Null(anonymous.Longitude); Assert.Null(anonymous.Observacao);
        Assert.Equal(HttpStatusCode.NoContent, (await Delete(contact)).StatusCode);
        using var scope = factory.Services.CreateScope(); var database = scope.ServiceProvider.GetRequiredService<HoodiDbContext>();
        Assert.Null((await database.DespachosPanico.SingleAsync()).ContatoId);
        Assert.True(await database.Emails.AnyAsync(x => x.Id == mailId));
    }

    [Fact]
    public async Task AcceptedExpoWithoutReceipt_MustRemainAcceptedAndStopPollingAfter24Hours()
    {
        using var factory = new AuthFactory(); using var owner = factory.Client(); using var contact = factory.Client();
        var a = await PanicoAndContaIntegrationTests.Seed(factory, owner, "owner"); var b = await PanicoAndContaIntegrationTests.Seed(factory, contact, "contact");
        await PanicoAndContaIntegrationTests.Link(factory, a, b);
        var result = await PanicoAndContaIntegrationTests.Create(owner, Guid.NewGuid());
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HoodiDbContext>();
        var gateway = new NoReceiptGateway(); var worker = new PanicoDispatchProcessor(db, factory.Clock, scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(), gateway);
        await worker.ProcessAsync(true, default); Assert.Equal(1, gateway.Calls);
        factory.Clock.Advance(TimeSpan.FromHours(24)); await worker.ProcessAsync(true, default); await worker.ProcessAsync(true, default);
        Assert.Equal(1, gateway.Calls); Assert.Equal("sem_recibo", (await db.DespachosPanico.SingleAsync()).Estado);
        var summary = (await scope.ServiceProvider.GetRequiredService<IPanicoService>().StatusAsync(a, result.PedidoId, default)).Resumo;
        Assert.Equal("aceito_expo", summary.Estado); Assert.Equal(1, summary.AceitosExpo); Assert.Equal(0, summary.FalhosOuCancelados); Assert.Equal(0, summary.AceitosProvedor);
    }
    private static Task<HttpResponseMessage> Delete(HttpClient client) => client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/v1/conta")
    { Content = JsonContent.Create(new { senha = "synthetic-password-123" }) });
    private sealed class NoReceiptGateway : IExpoGateway
    {
        public int Calls;
        public Task<ExpoResult> SendAsync(string token, Guid pedidoId, CancellationToken ct) { Calls++; return Task.FromResult(new ExpoResult("aceito_expo", "synthetic-ticket")); }
        public Task<ExpoResult> ReceiptAsync(string ticket, CancellationToken ct) { Calls++; return Task.FromResult(new ExpoResult("retry")); }
    }
}
