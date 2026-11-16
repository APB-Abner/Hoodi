using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Hoodi.Application.Panico;
using Hoodi.Infrastructure.Panico;
using Hoodi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hoodi.Tests;

public sealed class ExpoDispatchTests
{
    [Fact]
    public async Task RealGateway_UsesOnlyGenericPayload_SeparatesTicketReceiptAndOpening()
    {
        using var factory = new AuthFactory(); using var alice = factory.Client(); using var bob = factory.Client();
        var a = await PanicoAndContaIntegrationTests.Seed(factory, alice, "alice"); var b = await PanicoAndContaIntegrationTests.Seed(factory, bob, "bob");
        await PanicoAndContaIntegrationTests.Link(factory, a, b);
        var request = await PanicoAndContaIntegrationTests.Create(alice, Guid.NewGuid());
        var handler = new ExpoHttpStandIn();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HoodiDbContext>();
        var processor = new PanicoDispatchProcessor(db, factory.Clock, scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(), new ExpoGateway(new HttpClient(handler)));
        await processor.ProcessAsync(true, default);
        Assert.Equal("aceito_expo", (await db.DespachosPanico.SingleAsync()).Estado);
        Assert.Single(handler.Paths); Assert.EndsWith("/send", handler.Paths[0]);
        using var payload = JsonDocument.Parse(handler.Bodies[0]);
        var data = payload.RootElement.GetProperty("data");
        Assert.Equal(new[] { "pedidoId", "tipo" }, data.EnumerateObject().Select(x => x.Name).Order().ToArray());
        Assert.Equal(request.PedidoId, data.GetProperty("pedidoId").GetGuid());
        Assert.DoesNotContain("alice", handler.Bodies[0]); Assert.DoesNotContain("latitude", handler.Bodies[0]);
        factory.Clock.Advance(TimeSpan.FromMinutes(15)); await processor.ProcessAsync(true, default);
        Assert.Equal("aceito_provedor", (await db.DespachosPanico.SingleAsync()).Estado);
        Assert.Null((await db.DespachosPanico.SingleAsync()).AbertoEm);
        Assert.EndsWith("/getReceipts", handler.Paths[1]);
        var status = await scope.ServiceProvider.GetRequiredService<IPanicoService>().StatusAsync(a, request.PedidoId, default);
        Assert.Equal("aceito_provedor_push", status.Resumo.Estado); Assert.Equal(0, status.Resumo.AbertosNoApp);
    }

    [Theory]
    [InlineData("unavailable", "pendente", true)]
    [InlineData("malformed", "pendente", true)]
    [InlineData("timeout", "pendente", true)]
    [InlineData("unregistered", "falho", false)]
    [InlineData("revoked", "cancelado", true)]
    [InlineData("logout", "cancelado", false)]
    public async Task RealGateway_FailuresAndRevocationNeverClaimReceipt(string scenario, string expected, bool active)
    {
        using var factory = new AuthFactory(); using var alice = factory.Client(); using var bob = factory.Client();
        var a = await PanicoAndContaIntegrationTests.Seed(factory, alice, "alice"); var b = await PanicoAndContaIntegrationTests.Seed(factory, bob, "bob");
        var link = await PanicoAndContaIntegrationTests.Link(factory, a, b);
        await PanicoAndContaIntegrationTests.Create(alice, Guid.NewGuid());
        if (scenario == "revoked") Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/v1/contatos/{link}")).StatusCode);
        if (scenario == "logout") Assert.Equal(HttpStatusCode.NoContent, (await bob.PostAsJsonAsync("/v1/auth/sair", new { instalacaoId = "phone" })).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HoodiDbContext>();
        var handler = new ExpoHttpStandIn { Scenario = scenario };
        var processor = new PanicoDispatchProcessor(db, factory.Clock, scope.ServiceProvider.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>(), new ExpoGateway(new HttpClient(handler)));
        await processor.ProcessAsync(true, default);
        var dispatch = await db.DespachosPanico.SingleAsync(); Assert.Equal(expected, dispatch.Estado); Assert.Null(dispatch.AbertoEm);
        Assert.Equal(active, (await db.TokensPush.SingleAsync()).Ativo);
        if (scenario is "revoked" or "logout") Assert.Empty(handler.Paths);
        else Assert.Single(handler.Paths);
    }

    // HTTP boundary double based on the official Expo ticket/receipt envelopes; no outbound traffic.
    private sealed class ExpoHttpStandIn : HttpMessageHandler
    {
        public string Scenario { get; init; } = "success";
        public List<string> Paths { get; } = [];
        public List<string> Bodies { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Paths.Add(request.RequestUri!.AbsolutePath); Bodies.Add(await request.Content!.ReadAsStringAsync(ct));
            if (Scenario == "timeout") throw new TaskCanceledException("SYNTHETIC_TIMEOUT");
            var status = Scenario == "unavailable" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
            var body = Scenario switch
            {
                "malformed" => "<html>synthetic failure</html>",
                "unregistered" => "{\"data\":{\"status\":\"error\",\"details\":{\"error\":\"DeviceNotRegistered\"}}}",
                _ => request.RequestUri.AbsolutePath.EndsWith("/send") ? "{\"data\":{\"status\":\"ok\",\"id\":\"synthetic-ticket\"}}" : "{\"data\":{\"synthetic-ticket\":{\"status\":\"ok\"}}}"
            };
            return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        }
    }
}
