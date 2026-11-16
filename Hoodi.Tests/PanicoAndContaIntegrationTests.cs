using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Hoodi.Application.Auth;
using Hoodi.Application.Panico;
using Hoodi.Domain.Entities;
using Hoodi.Infrastructure.Persistence;
using Hoodi.Infrastructure.Panico;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hoodi.Tests;

public sealed class PanicoAndContaIntegrationTests
{
    [Fact]
    public void PanicWireFormatMustDeserializeAndRejectUnknownFields()
    {
        var options = new System.Text.Json.JsonSerializerOptions(System.Text.Json.JsonSerializerDefaults.Web);
        var valid = System.Text.Json.JsonSerializer.Serialize(new { idempotencyKey = Guid.NewGuid(), origem = "app" }, options);
        Assert.Equal("app", System.Text.Json.JsonSerializer.Deserialize<PanicoEntrada>(valid, options)!.Origem);
        Assert.Throws<System.Text.Json.JsonException>(() => System.Text.Json.JsonSerializer.Deserialize<PanicoEntrada>("{\"origem\":\"app\",\"usuarioId\":1}", options));
    }
    [Fact]
    public async Task DeleteBody_WrongPasswordProvesBinding_ThenCorrectPasswordDeletesAtomically()
    {
        using var factory = new AuthFactory();
        using var alice = factory.Client(); using var bob = factory.Client();
        var a = await Seed(factory, alice, "alice"); var b = await Seed(factory, bob, "bob");
        await Link(factory, a, b); await Link(factory, b, a);
        var request = await Create(alice, Guid.NewGuid());
        using (var setup = factory.Services.CreateScope())
        {
            var setupDb = setup.ServiceProvider.GetRequiredService<HoodiDbContext>();
            setupDb.Convites.Add(new ConviteContato(a, "SYNTHETIC_CODE", factory.Clock.GetUtcNow(), TimeSpan.FromHours(1)));
            setupDb.Redefinicoes.Add(new RedefinicaoSenha(a, "SYNTHETIC_RESET", factory.Clock.GetUtcNow()));
            setupDb.Cadastros.Add(new CadastroPendente("alice@example.invalid", "SYNTHETIC_PENDING", factory.Clock.GetUtcNow()));
            await setupDb.SaveChangesAsync();
        }
        Assert.Equal(HttpStatusCode.Forbidden, (await Delete(alice, new { senha = "wrong-on-purpose" })).StatusCode);
        var wrong = await Delete(alice, new { senha = "wrong-on-purpose" });
        Assert.Contains("SENHA_INVALIDA", await wrong.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.BadRequest, (await Delete(alice, new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await Delete(alice, new { senha = "synthetic-password-123", usuarioId = b })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await alice.GetAsync("/test/session")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await Delete(alice, new { senha = "synthetic-password-123" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await alice.GetAsync("/test/session")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync("/test/session")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/v1/panico/{request.PedidoId}")).StatusCode);
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HoodiDbContext>();
        Assert.False(await db.Usuarios.AnyAsync(x => x.Id == a));
        Assert.Empty(await db.Vinculos.ToListAsync());
        Assert.False(await db.Sessoes.AnyAsync(x => x.UsuarioId == a));
        Assert.False(await db.TokensPush.AnyAsync(x => x.UsuarioId == a));
        Assert.Empty(await db.Convites.ToListAsync()); Assert.Empty(await db.Redefinicoes.ToListAsync()); Assert.Empty(await db.Cadastros.ToListAsync());
        Assert.False(await db.RefreshTokens.AnyAsync(x => !db.Sessoes.Any(s => s.Id == x.SessaoId)));
        var anonymous = await db.PedidosPanico.SingleAsync();
        Assert.Null(anonymous.TitularId); Assert.Null(anonymous.IdempotencyKey); Assert.Null(anonymous.DadosCifrados);
        Assert.All(await db.DespachosPanico.ToListAsync(), x => { Assert.Null(x.ContatoId); Assert.Null(x.TokenHash); Assert.Null(x.VinculoId); });
    }

    [Fact]
    public async Task DeletionFailureAfterBulkWrites_MustRestoreAccountLinksAndPanicData()
    {
        using var factory = new AuthFactory(); using var alice = factory.Client(); using var bob = factory.Client();
        var a = await Seed(factory, alice, "alice"); var b = await Seed(factory, bob, "bob"); await Link(factory, a, b);
        var request = await Create(alice, Guid.NewGuid());
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HoodiDbContext>();
        var session = await db.Sessoes.SingleAsync(x => x.UsuarioId == a);
        var options = new DbContextOptionsBuilder<HoodiDbContext>().UseSqlite(db.Database.GetConnectionString()).AddInterceptors(new FailFinalSave()).Options;
        await using (var failing = new HoodiDbContext(options))
        {
            var service = new Hoodi.Infrastructure.Conta.ContaService(failing, scope.ServiceProvider.GetRequiredService<IPasswordHasher<Usuario>>(),
                scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>(), factory.Clock);
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExcluirAsync(a, session.Id, new("synthetic-password-123"), default));
        }
        db.ChangeTracker.Clear();
        Assert.True(await db.Usuarios.AnyAsync(x => x.Id == a)); Assert.True(await db.Sessoes.AnyAsync(x => x.UsuarioId == a));
        Assert.Single(await db.Vinculos.ToListAsync()); Assert.Equal(a, (await db.PedidosPanico.SingleAsync()).TitularId);
        Assert.Equal(b, (await db.DespachosPanico.SingleAsync()).ContatoId);
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync($"/v1/panico/{request.PedidoId}")).StatusCode);
    }

    [Fact]
    public async Task DatabaseConnections_MustEnableSecureDelete()
    {
        using var factory = new AuthFactory(); using var client = factory.Client();
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HoodiDbContext>();
        await db.Database.OpenConnectionAsync();
        await using var command = db.Database.GetDbConnection().CreateCommand(); command.CommandText = "PRAGMA secure_delete;";
        Assert.Equal(1L, await command.ExecuteScalarAsync());
    }

    private sealed class FailFinalSave : Microsoft.EntityFrameworkCore.Diagnostics.SaveChangesInterceptor
    {
        public override ValueTask<Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int>> SavingChangesAsync(
            Microsoft.EntityFrameworkCore.Diagnostics.DbContextEventData eventData,
            Microsoft.EntityFrameworkCore.Diagnostics.InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SYNTHETIC_FINAL_SAVE_FAILURE");
    }

    [Fact]
    public async Task Panic_ReplayFreezesDataAndRecipients_OpeningRequiresOriginalLiveLink()
    {
        using var factory = new AuthFactory();
        using var alice = factory.Client(); using var bob = factory.Client(); using var stranger = factory.Client();
        var a = await Seed(factory, alice, "alice"); var b = await Seed(factory, bob, "bob"); var c = await Seed(factory, stranger, "stranger");
        var link = await Link(factory, a, b);
        var key = Guid.NewGuid(); var original = await Create(alice, key);
        Assert.Equal("em_processamento", original.Resumo.Estado);
        await Link(factory, a, c);
        var replayResponse = await alice.PostAsJsonAsync("/v1/panico", new { idempotencyKey = key, origem = "watch" });
        Assert.Equal(HttpStatusCode.OK, replayResponse.StatusCode);
        var replay = (await replayResponse.Content.ReadFromJsonAsync<PanicoRegistrado>())!;
        Assert.True(replay.Reutilizado); Assert.Equal(original.PedidoId, replay.PedidoId); Assert.Equal("app", replay.Origem);
        Assert.Equal(original.RegistradoEm, replay.RegistradoEm); Assert.Equal(1, replay.Resumo.Total);
        var list = await bob.GetFromJsonAsync<PanicoRecebidoResumo[]>("/v1/panico/recebidos"); Assert.Single(list!);
        Assert.DoesNotContain("latitude", await (await bob.GetAsync("/v1/panico/recebidos")).Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await stranger.GetAsync($"/v1/panico/{original.PedidoId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/v1/panico/{original.PedidoId}/status")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await bob.PostAsync($"/v1/panico/{original.PedidoId}/abertura", null)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await bob.PostAsync($"/v1/panico/{original.PedidoId}/abertura", null)).StatusCode);
        Assert.Equal("aberto_no_app", (await alice.GetFromJsonAsync<PanicoStatus>($"/v1/panico/{original.PedidoId}/status"))!.Resumo.Estado);
        Assert.Equal(HttpStatusCode.NoContent, (await alice.DeleteAsync($"/v1/contatos/{link}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/v1/panico/{original.PedidoId}")).StatusCode);
        Assert.Empty((await bob.GetFromJsonAsync<PanicoRecebidoResumo[]>("/v1/panico/recebidos"))!);
        await Link(factory, a, b); // New consent must not resurrect access to a previous request.
        Assert.Equal(HttpStatusCode.NotFound, (await bob.GetAsync($"/v1/panico/{original.PedidoId}")).StatusCode);
    }

    [Fact]
    public async Task Panic_ConcurrentReplayCreatesOneRequestAndNoExtraDispatches()
    {
        using var factory = new AuthFactory(); using var client = factory.Client();
        await Seed(factory, client, "alice"); var key = Guid.NewGuid();
        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.PostAsJsonAsync("/v1/panico", new { idempotencyKey = key, origem = "app" })));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Accepted);
        Assert.Equal(3, responses.Count(x => x.StatusCode == HttpStatusCode.OK));
        var values = await Task.WhenAll(responses.Select(x => x.Content.ReadFromJsonAsync<PanicoRegistrado>()));
        Assert.Single(values.Select(x => x!.PedidoId).Distinct());
        Assert.All(values, x => Assert.Equal("sem_destinatarios", x!.Resumo.Estado));
    }

    [Theory]
    [InlineData("esp32", null, null, "ORIGEM_CREDENCIAL_INCOMPATIVEL")]
    [InlineData("app", 1d, null, "COORDENADAS_INVALIDAS")]
    [InlineData("app", 91d, 1d, "COORDENADAS_INVALIDAS")]
    public async Task Panic_InvalidInputsPersistNothing(string origin, double? lat, double? lon, string code)
    {
        using var factory = new AuthFactory(); using var client = factory.Client(); await Seed(factory, client, "alice");
        var response = await client.PostAsJsonAsync("/v1/panico", new { idempotencyKey = Guid.NewGuid(), origem = origin, latitude = lat, longitude = lon });
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode); Assert.Contains(code, await response.Content.ReadAsStringAsync());
        using var scope = factory.Services.CreateScope(); Assert.Empty(await scope.ServiceProvider.GetRequiredService<HoodiDbContext>().PedidosPanico.ToListAsync());
    }

    [Fact]
    public async Task Panic_PositionUses24HourDefault_IsEncrypted_ExpiresAndIsPhysicallyPurged()
    {
        using var factory = new AuthFactory(); using var client = factory.Client(); var id = await Seed(factory, client, "alice");
        var input = new { idempotencyKey = Guid.NewGuid(), origem = "app", latitude = -23.5, longitude = -46.6, observacao = "SYNTHETIC_PRIVATE_NOTE" };
        var result = await client.PostAsJsonAsync("/v1/panico", input);
        Assert.Equal(HttpStatusCode.Accepted, result.StatusCode); var value = (await result.Content.ReadFromJsonAsync<PanicoRegistrado>())!;
        var detail = await client.GetFromJsonAsync<PanicoDetalhe>($"/v1/panico/{value.PedidoId}"); Assert.True(detail!.LocalizacaoDisponivel);
        using (var scope = factory.Services.CreateScope())
            Assert.DoesNotContain("SYNTHETIC_PRIVATE_NOTE", (await scope.ServiceProvider.GetRequiredService<HoodiDbContext>().PedidosPanico.SingleAsync()).DadosCifrados!);
        factory.Clock.Advance(TimeSpan.FromHours(24));
        using (var expired = factory.Services.CreateScope()) detail = await expired.ServiceProvider.GetRequiredService<IPanicoService>().DetalheAsync(id, value.PedidoId, default);
        Assert.False(detail!.LocalizacaoDisponivel); Assert.Null(detail.Observacao);
        using (var replayScope = factory.Services.CreateScope())
        {
            var replay = await replayScope.ServiceProvider.GetRequiredService<IPanicoService>().RegistrarAsync(id,
                new(input.idempotencyKey, "watch", 12, 13, "MUST_NOT_REPLACE"), default);
            Assert.True(replay.Reutilizado); Assert.Equal(value.PedidoId, replay.PedidoId); Assert.Equal("app", replay.Origem);
        }
        using var purge = factory.Services.CreateScope();
        await purge.ServiceProvider.GetRequiredService<PanicoDispatchProcessor>().ProcessAsync(false, default);
        Assert.Null((await purge.ServiceProvider.GetRequiredService<HoodiDbContext>().PedidosPanico.SingleAsync()).DadosCifrados);
    }

    [Theory]
    [InlineData("{\"origem\":\"app\"}", 400)]
    [InlineData("{\"idempotencyKey\":\"00000000-0000-0000-0000-000000000000\",\"origem\":\"app\"}", 400)]
    [InlineData("{\"idempotencyKey\":\"11111111-1111-4111-8111-111111111111\",\"origem\":\"app\",\"latitude\":null}", 422)]
    [InlineData("{\"idempotencyKey\":\"11111111-1111-4111-8111-111111111111\",\"origem\":\"app\",\"usuarioId\":\"other\"}", 400)]
    public async Task Panic_RejectsMissingIdentityKeyAndMalformedCoordinateStructure(string json, int status)
    {
        using var factory = new AuthFactory(); using var client = factory.Client(); await Seed(factory, client, "alice");
        var response = await client.PostAsync("/v1/panico", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(status, (int)response.StatusCode);
    }

    internal static async Task<Guid> Seed(AuthFactory factory, HttpClient client, string name)
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HoodiDbContext>();
        var user = new Usuario(name, name + "@example.invalid", "pending", factory.Clock.GetUtcNow());
        user.AlterarSenhaHash(scope.ServiceProvider.GetRequiredService<IPasswordHasher<Usuario>>().HashPassword(user, "synthetic-password-123"));
        db.Usuarios.Add(user); await db.SaveChangesAsync();
        var response = await client.PostAsJsonAsync("/v1/auth/login", new { email = user.Email, senha = "synthetic-password-123" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); var tokens = (await response.Content.ReadFromJsonAsync<TokensAuth>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return user.Id;
    }
    internal static async Task<Guid> Link(AuthFactory factory, Guid owner, Guid contact)
    {
        using var scope = factory.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<HoodiDbContext>();
        var link = new VinculoContato(owner, contact, factory.Clock.GetUtcNow()); db.Vinculos.Add(link);
        var crypto = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("hoodi.push-token.v1");
        if (!await db.TokensPush.AnyAsync(x => x.UsuarioId == contact)) db.TokensPush.Add(new TokenPush(contact, "phone", "android", "SYNTHETIC_" + contact, crypto.Protect("ExpoPushToken[SYNTHETIC]"), factory.Clock.GetUtcNow()));
        await db.SaveChangesAsync(); return link.Id;
    }
    internal static async Task<PanicoRegistrado> Create(HttpClient client, Guid key)
    {
        var result = await client.PostAsJsonAsync("/v1/panico", new { idempotencyKey = key, origem = "app" });
        Assert.Equal(HttpStatusCode.Accepted, result.StatusCode); return (await result.Content.ReadFromJsonAsync<PanicoRegistrado>())!;
    }
    private static Task<HttpResponseMessage> Delete(HttpClient client, object body) => client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, "/v1/conta") { Content = JsonContent.Create(body) });
}
