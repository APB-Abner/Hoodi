using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Hoodi.Application.Auth;
using Hoodi.Infrastructure.Auth;
using Hoodi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Hoodi.Tests;

public sealed class AuthIntegrationTests
{
    [Fact]
    public async Task Cadastro_Confirmacao_Login_Replay_Reset_DeveInvalidarTokensAnteriores()
    {
        using var factory = new AuthFactory();
        using var client = factory.Client();
        const string email = "user@example.invalid";
        var cadastro = await client.PostAsJsonAsync("/v1/auth/registrar", new { email });
        Assert.Equal(HttpStatusCode.Accepted, cadastro.StatusCode);
        Assert.DoesNotContain("token", await cadastro.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/auth/login", new { email, senha = "test-password-123" })).StatusCode);
        var token = await factory.TokenAsync("confirmar_email");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/v1/auth/confirmar-email",
            new { token, nome = "Test User", senha = "test-password-123" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/auth/confirmar-email",
            new { token, nome = "Test User", senha = "other-password-123" })).StatusCode);
        var login = await Login(client, email);
        Assert.Equal(HttpStatusCode.OK, (await Probe(client, login.AccessToken)).StatusCode);
        var first = await Refresh(client, login.RefreshToken);
        var replay = await Refresh(client, login.RefreshToken);
        Assert.Equal(first, replay);
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/v1/auth/solicitar-redefinicao", new { email })).StatusCode);
        var reset = await factory.TokenAsync("reset_senha");
        // A confirmation token cannot reset a password.
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/auth/redefinir-senha",
            new { token, novaSenha = "new-password-123" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/v1/auth/redefinir-senha",
            new { token = reset, novaSenha = "new-password-123" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Probe(client, first.AccessToken)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/auth/renovar", new { refreshToken = first.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/auth/redefinir-senha",
            new { token = reset, novaSenha = "new-password-456" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/auth/login", new { email, senha = "test-password-123" })).StatusCode);
        var novoLogin = await client.PostAsJsonAsync("/v1/auth/login", new { email, senha = "new-password-123" });
        Assert.Equal(HttpStatusCode.OK, novoLogin.StatusCode);
    }

    [Fact]
    public async Task Refresh_ReutilizadoDepoisDaTolerancia_DeveRevogarFamilia()
    {
        using var factory = new AuthFactory();
        using var client = factory.Client();
        await Cadastrar(factory, client);
        var login = await Login(client, "user@example.invalid");
        var novo = await Refresh(client, login.RefreshToken);
        factory.Clock.Advance(TimeSpan.FromSeconds(45));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/auth/renovar", new { refreshToken = login.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/auth/renovar", new { refreshToken = novo.RefreshToken })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Probe(client, novo.AccessToken)).StatusCode);
    }

    [Fact]
    public async Task Cadastro_Reemitido_DeveInvalidarAnteriorESerAtomicoNaConfirmacaoConcorrente()
    {
        using var factory = new AuthFactory();
        using var client = factory.Client();
        await client.PostAsJsonAsync("/v1/auth/registrar", new { email = "user@example.invalid" });
        var antigo = await factory.TokenAsync("confirmar_email");
        factory.Clock.Advance(TimeSpan.FromMinutes(1));
        await client.PostAsJsonAsync("/v1/auth/registrar", new { email = "user@example.invalid" });
        var atual = await factory.TokenAsync("confirmar_email");
        Assert.NotEqual(antigo, atual);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/auth/confirmar-email",
            new { token = antigo, nome = "Test User", senha = "test-password-123" })).StatusCode);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => client.PostAsJsonAsync("/v1/auth/confirmar-email",
            new { token = atual, nome = "Test User", senha = "test-password-123" })));
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK);
        Assert.Single(results, x => x.StatusCode == HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task EntradaInvalida_DeveRetornarProblemDetailsSemEcoarValores()
    {
        using var factory = new AuthFactory();
        using var client = factory.Client();
        var response = await client.PostAsJsonAsync("/v1/auth/registrar", new { email = "invalid", senha = "must-not-appear" });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("must-not-appear", body);
        Assert.Contains("traceId", body);
        Assert.Contains("code", body);
    }

    [Fact]
    public async Task ContaExistenteENova_DevemTerMesmoAceiteSemAlterarSenha()
    {
        using var factory = new AuthFactory();
        using var client = factory.Client();
        await Cadastrar(factory, client);
        var existente = await client.PostAsJsonAsync("/v1/auth/registrar", new { email = "user@example.invalid" });
        var nova = await client.PostAsJsonAsync("/v1/auth/registrar", new { email = "new@example.invalid" });
        Assert.Equal(existente.StatusCode, nova.StatusCode);
        Assert.Equal(await existente.Content.ReadAsStringAsync(), await nova.Content.ReadAsStringAsync());
        await Login(client, "user@example.invalid");
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoodiDbContext>();
        Assert.Equal(1, await db.Usuarios.CountAsync());
        Assert.Equal(5, (await db.Database.GetAppliedMigrationsAsync()).Count());
    }

    [Fact]
    public async Task LimitePorEmail_DeveRetornar429MesmoComCadastroInexistente()
    {
        using var factory = new AuthFactory();
        using var client = factory.Client();
        for (var i = 0; i < 5; i++)
            Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/v1/auth/solicitar-redefinicao", new { email = "missing@example.invalid" })).StatusCode);
        var response = await client.PostAsJsonAsync("/v1/auth/solicitar-redefinicao", new { email = "missing@example.invalid" });
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
    }

    [Fact]
    public async Task Refresh_Concorrente_DeveRetornarMesmoParSemDuplaRotacao()
    {
        using var factory = new AuthFactory();
        using var client = factory.Client();
        await Cadastrar(factory, client);
        var login = await Login(client, "user@example.invalid");
        var respostas = await Task.WhenAll(Refresh(client, login.RefreshToken), Refresh(client, login.RefreshToken));
        Assert.Equal(respostas[0], respostas[1]);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoodiDbContext>();
        Assert.Equal(2, await db.RefreshTokens.CountAsync());
        Assert.Single(await db.RefreshTokens.Where(x => x.ReplayCifrado != null).ToListAsync());
    }

    [Fact]
    public async Task ConfirmacaoExpirada_DeveFalharSemCriarUsuario()
    {
        using var factory = new AuthFactory();
        using var client = factory.Client();
        await client.PostAsJsonAsync("/v1/auth/registrar", new { email = "user@example.invalid" });
        var token = await factory.TokenAsync("confirmar_email");
        factory.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/auth/confirmar-email",
            new { token, nome = "Test User", senha = "test-password-123" })).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<HoodiDbContext>().Usuarios.ToListAsync());
    }
    private static async Task Cadastrar(AuthFactory factory, HttpClient client)
    {
        Assert.Equal(HttpStatusCode.Accepted, (await client.PostAsJsonAsync("/v1/auth/registrar", new { email = "user@example.invalid" })).StatusCode);
        var token = await factory.TokenAsync("confirmar_email");
        Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/v1/auth/confirmar-email",
            new { token, nome = "Test User", senha = "test-password-123" })).StatusCode);
    }
    private static async Task<TokensAuth> Login(HttpClient client, string email)
    {
        var response = await client.PostAsJsonAsync("/v1/auth/login", new { email, senha = "test-password-123" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TokensAuth>())!;
    }
    private static async Task<TokensAuth> Refresh(HttpClient client, string refresh)
    {
        var response = await client.PostAsJsonAsync("/v1/auth/renovar", new { refreshToken = refresh });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<TokensAuth>())!;
    }
    private static Task<HttpResponseMessage> Probe(HttpClient client, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/test/session");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client.SendAsync(request);
    }
}

public sealed class AuthFactory : WebApplicationFactory<Program>
{
    private readonly string path = Path.Combine(Path.GetTempPath(), "hoodi-auth-test-" + Guid.NewGuid().ToString("N") + ".db");
    private readonly string keysPath = Path.Combine(Path.GetTempPath(), "hoodi-auth-keys-" + Guid.NewGuid().ToString("N"));
    private readonly string signingKey = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(64));
    public TestClock Clock { get; } = new();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        { ["Database:ConnectionString"] = "Data Source=" + path, ["DataProtection:KeysPath"] = keysPath, ["Jwt:SigningKey"] = signingKey }));
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);
            services.AddControllers().AddApplicationPart(typeof(SessionProbeController).Assembly);
        });
    }
    public HttpClient Client() => CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
    public async Task<string> TokenAsync(string proposito)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoodiDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("hoodi.auth.email-outbox.v1");
        var mensagens = (await db.Emails.ToListAsync()).Select(x => (x.CriadoEmUtc,
            Data: JsonSerializer.Deserialize<EmailAutenticacao>(protector.Unprotect(x.ConteudoCifrado))!));
        return mensagens.Where(x => x.Data.Proposito == proposito).OrderByDescending(x => x.CriadoEmUtc).First().Data.Token;
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(path + suffix);
        if (Directory.Exists(keysPath))
        {
            foreach (var file in Directory.EnumerateFiles(keysPath)) File.Delete(file);
            Directory.Delete(keysPath);
        }
    }
}
public sealed class TestClock : TimeProvider
{
    private DateTimeOffset now = DateTimeOffset.UtcNow;
    public override DateTimeOffset GetUtcNow() => now;
    public void Advance(TimeSpan duration) => now += duration;
}
[ApiController, Authorize, Route("test/session")]
public sealed class SessionProbeController : ControllerBase
{
    [HttpGet] public IActionResult Get() => Ok();
}




