using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Hoodi.Application.Auth;
using Hoodi.Application.Contatos;
using Hoodi.Application.Push;
using Hoodi.Domain.Entities;
using Hoodi.Domain.Enums;
using Hoodi.Domain.Exceptions;
using Hoodi.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hoodi.Tests;

public sealed class ContactsAndPushIntegrationTests
{
    [Fact]
    public async Task Endpoints_DeveExigirJwt()
    {
        using var factory = new AuthFactory();
        using var client = factory.Client();
        foreach (var (method, path) in new[]
        {
            (HttpMethod.Get, "/v1/contatos"), (HttpMethod.Get, "/v1/contatos/convites"),
            (HttpMethod.Post, "/v1/contatos/convites"), (HttpMethod.Post, "/v1/contatos/convites/resgatar"), (HttpMethod.Delete, "/v1/contatos/convites/" + Guid.NewGuid()),
            (HttpMethod.Delete, "/v1/contatos/" + Guid.NewGuid()), (HttpMethod.Put, "/v1/push/tokens"),
            (HttpMethod.Delete, "/v1/push/tokens/" + Guid.NewGuid())
        })
        {
            using var response = await client.SendAsync(new HttpRequestMessage(method, path));
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    [Fact]
    public async Task Convites_DeveIsolarTitularOcultarCodigoEPermitirCancelamento()
    {
        using var factory = new AuthFactory();
        using var a = await CreateUser(factory, "Alice");
        using var b = await CreateUser(factory, "Bob");
        var convite = await Emitir(a.Client);
        var lista = await a.Client.GetAsync("/v1/contatos/convites");
        var json = await lista.Content.ReadAsStringAsync();
        Assert.DoesNotContain(convite.Codigo, json);
        Assert.DoesNotContain("codigo", json, StringComparison.OrdinalIgnoreCase);
        Assert.Single((await lista.Content.ReadFromJsonAsync<ConvitePendenteResumo[]>())!);
        Assert.Empty((await b.Client.GetFromJsonAsync<ConvitePendenteResumo[]>("/v1/contatos/convites"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Client.DeleteAsync("/v1/contatos/convites/" + convite.ConviteId)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync("/v1/contatos/convites/" + convite.ConviteId)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await a.Client.DeleteAsync("/v1/contatos/convites/" + convite.ConviteId)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoodiDbContext>();
        var saved = await db.Convites.SingleAsync();
        Assert.Equal(EstadoConvite.Cancelado, saved.Estado);
        Assert.NotEqual(convite.Codigo, saved.CodigoHash);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(convite.Codigo))), saved.CodigoHash);
    }

    [Fact]
    public async Task Expiracao_DeveSerConsultadaNoSqliteComLimiteExato()
    {
        using var factory = new AuthFactory();
        using var user = await CreateUser(factory, "Alice");
        var convite = await Emitir(user.Client);
        Assert.Equal(factory.Clock.GetUtcNow().AddHours(1), convite.ExpiraEm);
        factory.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Empty((await user.Client.GetFromJsonAsync<ConvitePendenteResumo[]>("/v1/contatos/convites"))!);
        Assert.Equal(HttpStatusCode.Conflict, (await user.Client.DeleteAsync("/v1/contatos/convites/" + convite.ConviteId)).StatusCode);
    }

    [Fact]
    public async Task Emissao_DeveLimitarPorUsuarioSemBloquearOutroUsuario()
    {
        using var factory = new AuthFactory();
        using var a = await CreateUser(factory, "Alice");
        using var b = await CreateUser(factory, "Bob");
        for (var i = 0; i < 5; i++) await Emitir(a.Client);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await a.Client.PostAsync("/v1/contatos/convites", null)).StatusCode);
        await Emitir(b.Client);
    }

    [Fact]
    public async Task Aceite_DeveSerUnidirecionalSemEmailERevogavelComHistorico()
    {
        using var factory = new AuthFactory();
        using var a = await CreateUser(factory, "Alice");
        using var b = await CreateUser(factory, "Bob");
        using var c = await CreateUser(factory, "Carol");
        var convite = await Emitir(a.Client);
        var vinculo = await Resgatar(factory, b.Id, convite.Codigo);
        Assert.Equal(a.Id, vinculo!.TitularId);
        Assert.Equal(b.Id, vinculo.ContatoId);
        var listaA = await a.Client.GetAsync("/v1/contatos");
        var json = await listaA.Content.ReadAsStringAsync();
        Assert.DoesNotContain("email", json, StringComparison.OrdinalIgnoreCase);
        var resumoA = Assert.Single((await listaA.Content.ReadFromJsonAsync<VinculoResumo[]>())!);
        Assert.Equal("Bob", resumoA.NomeExibicao);
        Assert.Equal("titular", resumoA.PapelDoSolicitante);
        var resumoB = Assert.Single((await b.Client.GetFromJsonAsync<VinculoResumo[]>("/v1/contatos"))!);
        Assert.Equal("Alice", resumoB.NomeExibicao);
        Assert.Equal("contato", resumoB.PapelDoSolicitante);
        Assert.Equal(a.Id, resumoB.TitularId);
        Assert.Empty((await c.Client.GetFromJsonAsync<VinculoResumo[]>("/v1/contatos"))!);
        Assert.Equal(HttpStatusCode.NotFound, (await c.Client.DeleteAsync("/v1/contatos/" + vinculo.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await b.Client.DeleteAsync("/v1/contatos/" + vinculo.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync("/v1/contatos/" + vinculo.Id)).StatusCode);
        var novo = await Emitir(a.Client);
        var segundo = await Resgatar(factory, b.Id, novo.Codigo);
        Assert.NotEqual(vinculo.Id, segundo!.Id);
        var historico = (await a.Client.GetFromJsonAsync<VinculoResumo[]>("/v1/contatos"))!;
        Assert.Equal(2, historico.Length);
        Assert.Single(historico, x => x.Estado == "aceito");
        Assert.Single(historico, x => x.Estado == "revogado");
    }

    [Fact]
    public async Task ResgateConcorrente_DeveCriarUmSoVinculoEManterCodigoDuplicadoPendente()
    {
        using var factory = new AuthFactory();
        using var a = await CreateUser(factory, "Alice");
        using var b = await CreateUser(factory, "Bob");
        var primeiro = await Emitir(a.Client);
        var segundo = await Emitir(a.Client);
        async Task<bool> Attempt(string codigo)
        {
            try { await Resgatar(factory, b.Id, codigo); return true; }
            catch (DomainException error) { Assert.Equal("VINCULO_DUPLICADO", error.Code); return false; }
        }
        var results = await Task.WhenAll(Attempt(primeiro.Codigo), Attempt(segundo.Codigo));
        Assert.Single(results, x => x);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoodiDbContext>();
        Assert.Single(await db.Vinculos.Where(x => x.Status == StatusVinculo.Aceito).ToListAsync());
        Assert.Single(await db.Convites.Where(x => x.Estado == EstadoConvite.Pendente).ToListAsync());
    }

    [Fact]
    public async Task Push_DeveAtualizarReativarECifrarSemExporTokenNaResposta()
    {
        using var factory = new AuthFactory();
        using var user = await CreateUser(factory, "Alice");
        const string token = "ExpoPushToken[SYNTHETIC_PUSH_ONE]";
        var response = await user.Client.PutAsJsonAsync("/v1/push/tokens", new TokenPushEntrada(token, "android", "install-one"));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(token, body);
        var first = (await response.Content.ReadFromJsonAsync<TokenPushResumo>())!;
        Assert.True(first.Ativo);
        var updated = await RegistrarPush(user.Client, token, "install-one");
        Assert.Equal(first.Id, updated.Id);
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync("/v1/push/tokens/" + first.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await user.Client.DeleteAsync("/v1/push/tokens/" + first.Id)).StatusCode);
        const string replacement = "ExponentPushToken[SYNTHETIC_PUSH_TWO]";
        var reactivated = await RegistrarPush(user.Client, replacement, "install-one");
        Assert.Equal(first.Id, reactivated.Id);
        Assert.True(reactivated.Ativo);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoodiDbContext>();
        var saved = await db.TokensPush.SingleAsync();
        Assert.NotEqual(replacement, saved.TokenCifrado);
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("hoodi.push-token.v1");
        Assert.Equal(replacement, protector.Unprotect(saved.TokenCifrado));
    }

    [Fact]
    public async Task Push_TrocaDeConta_DeveDesativarAssociacaoAnteriorSemDarAcessoAoRegistroAlheio()
    {
        using var factory = new AuthFactory();
        using var a = await CreateUser(factory, "Alice");
        using var b = await CreateUser(factory, "Bob");
        const string token = "ExpoPushToken[SYNTHETIC_SHARED_PHONE]";
        var first = await RegistrarPush(a.Client, token, "shared-install");
        var second = await RegistrarPush(b.Client, token, "shared-install");
        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await a.Client.DeleteAsync("/v1/push/tokens/" + second.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.Client.DeleteAsync("/v1/push/tokens/" + first.Id)).StatusCode);
        using var scope = factory.Services.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<HoodiDbContext>().TokensPush.ToListAsync();
        Assert.False(rows.Single(x => x.Id == first.Id).Ativo);
        Assert.True(rows.Single(x => x.Id == second.Id).Ativo);
    }

    [Fact]
    public async Task Push_RegistroConcorrente_DeveManterUmaSoAssociacaoAtiva()
    {
        using var factory = new AuthFactory();
        using var a = await CreateUser(factory, "Alice");
        using var b = await CreateUser(factory, "Bob");
        const string token = "ExpoPushToken[SYNTHETIC_SHARED_PHONE]";
        await Task.WhenAll(RegistrarPush(a.Client, token, "install-a"), RegistrarPush(b.Client, token, "install-b"));
        using var scope = factory.Services.CreateScope();
        var rows = await scope.ServiceProvider.GetRequiredService<HoodiDbContext>().TokensPush.ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Single(rows, x => x.Ativo);
    }

    [Theory]
    [InlineData("invalid-token", "android", "install")]
    [InlineData("ExpoPushToken[test]", "windows", "install")]
    [InlineData("ExpoPushToken[test]", "android", " ")]
    public async Task Push_Invalido_DeveRetornar422SemEcoarValor(string token, string plataforma, string instalacao)
    {
        using var factory = new AuthFactory();
        using var user = await CreateUser(factory, "Alice");
        var response = await user.Client.PutAsJsonAsync("/v1/push/tokens", new TokenPushEntrada(token, plataforma, instalacao));
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        Assert.DoesNotContain(token, await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Resgate_InvalidoOuReutilizado_NaoPodeCriarVinculoAdicional()
    {
        using var factory = new AuthFactory();
        using var a = await CreateUser(factory, "Alice");
        using var b = await CreateUser(factory, "Bob");
        using var c = await CreateUser(factory, "Carol");
        var convite = await Emitir(a.Client);
        var self = await Assert.ThrowsAsync<DomainException>(() => Resgatar(factory, a.Id, convite.Codigo));
        Assert.Equal("VINCULO_PROPRIO", self.Code);
        var unknown = await Assert.ThrowsAsync<DomainException>(() => Resgatar(factory, b.Id, new string('F', 64)));
        Assert.Equal("CONVITE_INVALIDO", unknown.Code);
        await Resgatar(factory, b.Id, convite.Codigo);
        var reused = await Assert.ThrowsAsync<DomainException>(() => Resgatar(factory, c.Id, convite.Codigo));
        Assert.Equal("CONVITE_ENCERRADO", reused.Code);
        using var scope = factory.Services.CreateScope();
        Assert.Single(await scope.ServiceProvider.GetRequiredService<HoodiDbContext>().Vinculos.ToListAsync());
    }

    [Fact]
    public async Task VinculoInverso_DeveExigirOutroConviteESerIndependente()
    {
        using var factory = new AuthFactory();
        using var a = await CreateUser(factory, "Alice");
        using var b = await CreateUser(factory, "Bob");
        var ab = await Resgatar(factory, b.Id, (await Emitir(a.Client)).Codigo);
        var ba = await Resgatar(factory, a.Id, (await Emitir(b.Client)).Codigo);
        Assert.NotEqual(ab!.Id, ba!.Id);
        Assert.Equal(a.Id, ab.TitularId);
        Assert.Equal(b.Id, ba.TitularId);
        await a.Client.DeleteAsync("/v1/contatos/" + ab.Id);
        var history = (await b.Client.GetFromJsonAsync<VinculoResumo[]>("/v1/contatos"))!;
        Assert.Equal("revogado", history.Single(x => x.Id == ab.Id).Estado);
        Assert.Equal("aceito", history.Single(x => x.Id == ba.Id).Estado);
    }

    [Fact]
    public async Task MigrationIncremental_DevePreservarUsuarioDaBaseAnterior()
    {
        using var state = new DeploymentState();
        var options = new DbContextOptionsBuilder<HoodiDbContext>().UseSqlite("Data Source=" + state.DatabasePath).Options;
        await using var db = new HoodiDbContext(options);
        var migrator = Microsoft.EntityFrameworkCore.Infrastructure.AccessorExtensions.GetService<Microsoft.EntityFrameworkCore.Migrations.IMigrator>(db);
        await migrator.MigrateAsync(db.Database.GetMigrations().Single(x => x.EndsWith("InitialAuthentication")));
        var user = new Usuario("Existing User", "existing@example.invalid", "SYNTHETIC_HASH", DateTimeOffset.UnixEpoch);
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();
        await migrator.MigrateAsync();
        Assert.Equal(user.Id, (await db.Usuarios.SingleAsync()).Id);
        Assert.Equal("SYNTHETIC_HASH", (await db.Usuarios.SingleAsync()).SenhaHash);
        Assert.Empty(await db.Convites.ToListAsync());
        Assert.Empty(await db.TokensPush.ToListAsync());
        Assert.Equal(2, (await db.Database.GetAppliedMigrationsAsync()).Count());
    }
    [Theory]
    [InlineData("aceitar")]
    [InlineData("rejeitar")]
    public async Task ResgateHttp_ValidacaoDeCodigo_DevePrecederEmissorERejeicao(string decisao)
    {
        using var factory = new AuthFactory();
        using var a = await CreateUser(factory, "Alice");
        using var b = await CreateUser(factory, "Bob");
        var unknown = await ResgatarHttp(b.Client, new string('F', 64), decisao);
        Assert.Equal(HttpStatusCode.Conflict, unknown.StatusCode);
        var expired = await Emitir(a.Client);
        factory.Clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(HttpStatusCode.Conflict, (await ResgatarHttp(b.Client, expired.Codigo, decisao)).StatusCode);
        // The issuer of an expired code must also get 409, not the own-link 422.
        Assert.Equal(HttpStatusCode.Conflict, (await ResgatarHttp(a.Client, expired.Codigo, decisao)).StatusCode);
        var canceled = await Emitir(a.Client);
        await a.Client.DeleteAsync("/v1/contatos/convites/" + canceled.ConviteId);
        Assert.Equal(HttpStatusCode.Conflict, (await ResgatarHttp(b.Client, canceled.Codigo, decisao)).StatusCode);
    }

    [Theory]
    [InlineData("aceitar")]
    [InlineData("rejeitar")]
    public async Task ResgateHttp_Emissor_DeveReceber422SemConsumirConvite(string decisao)
    {
        using var factory = new AuthFactory();
        using var a = await CreateUser(factory, "Alice");
        var invitation = await Emitir(a.Client);
        var response = await ResgatarHttp(a.Client, invitation.Codigo, decisao);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var error = await response.Content.ReadAsStringAsync();
        Assert.Contains("VINCULO_PROPRIO", error);
        Assert.DoesNotContain(invitation.Codigo, error);
        Assert.Single((await a.Client.GetFromJsonAsync<ConvitePendenteResumo[]>("/v1/contatos/convites"))!);
        Assert.Equal(HttpStatusCode.NoContent, (await a.Client.DeleteAsync("/v1/contatos/convites/" + invitation.ConviteId)).StatusCode);
    }

    [Fact]
    public async Task ResgateHttp_AceiteDuplicado409_RejeicaoDuplicada204SemNovoVinculo()
    {
        using var factory = new AuthFactory();
        using var a = await CreateUser(factory, "Alice");
        using var b = await CreateUser(factory, "Bob");
        var first = await Emitir(a.Client);
        var accepted = await ResgatarHttp(b.Client, first.Codigo, "aceitar");
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        var link = (await accepted.Content.ReadFromJsonAsync<VinculoResumo>())!;
        Assert.Equal(a.Id, link.TitularId);
        Assert.Equal(b.Id, link.ContatoId);
        Assert.Equal("aceito", link.Estado);
        var repeated = await Emitir(a.Client);
        var duplicate = await ResgatarHttp(b.Client, repeated.Codigo, "aceitar");
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        Assert.Contains("VINCULO_DUPLICADO", await duplicate.Content.ReadAsStringAsync());
        var rejected = await ResgatarHttp(b.Client, repeated.Codigo, "rejeitar");
        Assert.Equal(HttpStatusCode.NoContent, rejected.StatusCode);
        Assert.Empty(await rejected.Content.ReadAsStringAsync());
        Assert.Empty((await a.Client.GetFromJsonAsync<ConvitePendenteResumo[]>("/v1/contatos/convites"))!);
        Assert.Equal(HttpStatusCode.Conflict, (await ResgatarHttp(b.Client, repeated.Codigo, "aceitar")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await ResgatarHttp(b.Client, repeated.Codigo, "rejeitar")).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoodiDbContext>();
        Assert.Single(await db.Vinculos.ToListAsync());
        Assert.Equal(EstadoConvite.Rejeitado, (await db.Convites.SingleAsync(x => x.Id == repeated.ConviteId)).Estado);
    }

    [Fact]
    public async Task RejeicaoHttp_SemVinculo_DeveEncerrarConviteESumirDaLista()
    {
        using var factory = new AuthFactory();
        using var a = await CreateUser(factory, "Alice");
        using var b = await CreateUser(factory, "Bob");
        var first = await Emitir(a.Client);
        var rejected = await ResgatarHttp(b.Client, first.Codigo, "rejeitar");
        Assert.Equal(HttpStatusCode.NoContent, rejected.StatusCode);
        Assert.Empty(await rejected.Content.ReadAsStringAsync());
        Assert.Empty((await a.Client.GetFromJsonAsync<ConvitePendenteResumo[]>("/v1/contatos/convites"))!);
        Assert.Empty((await a.Client.GetFromJsonAsync<VinculoResumo[]>("/v1/contatos"))!);
        var next = await Emitir(a.Client);
        Assert.Equal(HttpStatusCode.OK, (await ResgatarHttp(b.Client, next.Codigo, "aceitar")).StatusCode);
    }

    [Fact]
    public async Task ResgateHttp_Concorrente_DoMesmoCodigo_DeveTerUmAceiteEUm409()
    {
        using var factory = new AuthFactory();
        using var a = await CreateUser(factory, "Alice");
        using var b = await CreateUser(factory, "Bob");
        using var c = await CreateUser(factory, "Carol");
        var invite = await Emitir(a.Client);
        var responses = await Task.WhenAll(ResgatarHttp(b.Client, invite.Codigo, "aceitar"), ResgatarHttp(c.Client, invite.Codigo, "aceitar"));
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task OpenApi_DevePublicarPatch053ERespostasDoResgate()
    {
        using var factory = new AuthFactory();
        using var client = factory.Client();
        using var document = System.Text.Json.JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        Assert.Equal("0.5.3", document.RootElement.GetProperty("info").GetProperty("version").GetString());
        var responses = document.RootElement.GetProperty("paths").GetProperty("/v1/contatos/convites/resgatar")
            .GetProperty("post").GetProperty("responses");
        foreach (var status in new[] { "200", "204", "400", "409", "422" })
            Assert.True(responses.TryGetProperty(status, out _));
        Assert.False(responses.GetProperty("204").TryGetProperty("content", out _));
        foreach (var action in new[] { "registrar", "confirmar-email", "login", "renovar", "solicitar-redefinicao", "redefinir-senha" })
            Assert.True(document.RootElement.GetProperty("paths").GetProperty("/v1/auth/" + action).GetProperty("post")
                .GetProperty("responses").TryGetProperty("400", out _));
        foreach (var (action, status) in new[] { ("registrar", "202"), ("confirmar-email", "200"), ("login", "200"), ("renovar", "200"), ("solicitar-redefinicao", "202"), ("redefinir-senha", "200") })
            Assert.True(document.RootElement.GetProperty("paths").GetProperty("/v1/auth/" + action).GetProperty("post")
                .GetProperty("responses").TryGetProperty(status, out _));
        var code = document.RootElement.GetProperty("components").GetProperty("schemas")
            .GetProperty("ResgateConviteEntrada").GetProperty("properties").GetProperty("codigo");
        Assert.Equal(64, code.GetProperty("minLength").GetInt32());
        Assert.Equal(64, code.GetProperty("maxLength").GetInt32());
        Assert.Equal("^[0-9A-F]{64}$", code.GetProperty("pattern").GetString());
    }
    [Fact]
    public async Task CodigoComMinusculasEEspacos_DeveSerNormalizadoAntesDaValidacaoEHash()
    {
        using var factory = new AuthFactory();
        using var a = await CreateUser(factory, "Alice");
        using var b = await CreateUser(factory, "Bob");
        var invite = await Emitir(a.Client);
        Assert.Matches("^[0-9A-F]{64}$", invite.Codigo);
        var response = await b.Client.PostAsJsonAsync("/v1/contatos/convites/resgatar",
            new { codigo = "  " + invite.Codigo.ToLowerInvariant() + "  ", decisao = "aceitar" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("short")]
    [InlineData("long")]
    [InlineData("nonhex")]
    public async Task CodigoMalformado_DeveRetornar400SemConsumirConvite(string kind)
    {
        using var factory = new AuthFactory();
        using var a = await CreateUser(factory, "Alice");
        using var b = await CreateUser(factory, "Bob");
        var invite = await Emitir(a.Client);
        object body = kind switch
        {
            "missing" => new { decisao = "aceitar" },
            "short" => new { codigo = "ABC", decisao = "aceitar" },
            "long" => new { codigo = new string('A', 65), decisao = "aceitar" },
            _ => new { codigo = new string('Z', 64), decisao = "aceitar" }
        };
        var response = await b.Client.PostAsJsonAsync("/v1/contatos/convites/resgatar", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("DADOS_INVALIDOS", await response.Content.ReadAsStringAsync());
        Assert.Single((await a.Client.GetFromJsonAsync<ConvitePendenteResumo[]>("/v1/contatos/convites"))!);
    }
    [Theory]
    [InlineData("login")]
    [InlineData("renovar")]
    public async Task CorpoAuthInvalido_DeveTer400DocumentadoSemEcoarDados(string action)
    {
        using var factory = new AuthFactory();
        using var client = factory.Client();
        var response = await client.PostAsJsonAsync("/v1/auth/" + action, new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("DADOS_INVALIDOS", await response.Content.ReadAsStringAsync());
    }
    private static Task<HttpResponseMessage> ResgatarHttp(HttpClient client, string codigo, string decisao) =>
        client.PostAsJsonAsync("/v1/contatos/convites/resgatar", new ResgateConviteEntrada(codigo, decisao));
    private static async Task<NovoConvite> Emitir(HttpClient client)
    {
        var response = await client.PostAsync("/v1/contatos/convites", null);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<NovoConvite>())!;
    }
    private static async Task<VinculoResumo?> Resgatar(AuthFactory factory, Guid usuario, string codigo)
    {
        using var scope = factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IContatosService>().ResgatarAsync(usuario, new ResgateConviteEntrada(codigo, "aceitar"), default);
    }
    private static async Task<TokenPushResumo> RegistrarPush(HttpClient client, string token, string installation)
    {
        var response = await client.PutAsJsonAsync("/v1/push/tokens", new TokenPushEntrada(token, "android", installation));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TokenPushResumo>())!;
    }
    private static async Task<UserClient> CreateUser(AuthFactory factory, string nome)
    {
        var email = nome.ToLowerInvariant() + "@example.invalid";
        var client = factory.Client();
        Guid id;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<HoodiDbContext>();
            var user = new Usuario(nome, email, "pending", factory.Clock.GetUtcNow());
            user.AlterarSenhaHash(scope.ServiceProvider.GetRequiredService<IPasswordHasher<Usuario>>().HashPassword(user, "synthetic-password-123"));
            id = user.Id;
            db.Usuarios.Add(user);
            await db.SaveChangesAsync();
        }
        var login = await client.PostAsJsonAsync("/v1/auth/login", new { email, senha = "synthetic-password-123" });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var tokens = (await login.Content.ReadFromJsonAsync<TokensAuth>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return new UserClient(id, client);
    }
    private sealed record UserClient(Guid Id, HttpClient Client) : IDisposable
    {
        public void Dispose() => Client.Dispose();
    }
}






