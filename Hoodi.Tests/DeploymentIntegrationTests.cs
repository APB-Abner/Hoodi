using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Hoodi.Api.Auth;
using Hoodi.Api.Configuration;
using Hoodi.Application.Auth;
using Hoodi.Domain.Exceptions;
using Hoodi.Infrastructure.Auth;
using Hoodi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hoodi.Tests;

public sealed class DeploymentIntegrationTests
{
    [Fact]
    public async Task Scalar_DeveServirInterfaceEOpenApiDasSeisOperacoes()
    {
        using var state = new DeploymentState();
        using var host = new DeploymentFactory(state);
        using var client = host.Client();
        var scalar = await client.GetAsync("/scalar");
        if (scalar.StatusCode == HttpStatusCode.Found)
        {
            Assert.Equal("/scalar/", new Uri(client.BaseAddress!, scalar.Headers.Location!).AbsolutePath);
            scalar = await client.GetAsync(scalar.Headers.Location);
        }
        Assert.Equal(HttpStatusCode.OK, scalar.StatusCode);
        Assert.Equal("text/html", scalar.Content.Headers.ContentType?.MediaType);
        var html = await scalar.Content.ReadAsStringAsync();
        Assert.Contains("hoodi API", html);
        Assert.Contains("openapi/v1.json", html);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/scalar/scalar.js")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/scalar/scalar.aspnetcore.js")).StatusCode);
        var response = await client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        foreach (var action in new[] { "registrar", "confirmar-email", "login", "renovar", "solicitar-redefinicao", "redefinir-senha" })
            Assert.True(paths.GetProperty("/v1/auth/" + action).TryGetProperty("post", out _));
        Assert.False(paths.TryGetProperty("/weatherforecast", out _));
    }

    [Fact]
    public async Task Reinicio_Production_DevePreservarJwtReplayEEmailPendente()
    {
        using var state = new DeploymentState();
        TokensAuth login;
        TokensAuth rotated;
        string pending;
        using (var host = new DeploymentFactory(state))
        using (var client = host.Client())
        {
            await client.PostAsJsonAsync("/v1/auth/registrar", new { email = "user@example.invalid" });
            var token = await host.TokenAsync("user@example.invalid");
            Assert.Equal(HttpStatusCode.OK, (await client.PostAsJsonAsync("/v1/auth/confirmar-email",
                new { token, nome = "Test User", senha = "test-password-123" })).StatusCode);
            var response = await client.PostAsJsonAsync("/v1/auth/login", new { email = "user@example.invalid", senha = "test-password-123" });
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            login = (await response.Content.ReadFromJsonAsync<TokensAuth>())!;
            var refresh = await client.PostAsJsonAsync("/v1/auth/renovar", new { refreshToken = login.RefreshToken });
            Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
            rotated = (await refresh.Content.ReadFromJsonAsync<TokensAuth>())!;
            await client.PostAsJsonAsync("/v1/auth/registrar", new { email = "pending@example.invalid" });
            pending = await host.TokenAsync("pending@example.invalid");
        }
        using var restarted = new DeploymentFactory(state);
        using var second = restarted.Client();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/test/session");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", rotated.AccessToken);
        Assert.Equal(HttpStatusCode.OK, (await second.SendAsync(request)).StatusCode);
        var replay = await second.PostAsJsonAsync("/v1/auth/renovar", new { refreshToken = login.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(rotated, await replay.Content.ReadFromJsonAsync<TokensAuth>());
        Assert.Equal(pending, await restarted.TokenAsync("pending@example.invalid"));
        Assert.Equal(HttpStatusCode.OK, (await second.PostAsJsonAsync("/v1/auth/confirmar-email",
            new { token = pending, nome = "Pending User", senha = "test-password-123" })).StatusCode);
    }

    [Fact]
    public async Task ProxyConfiavel_DevePreservarHttpsESepararRateLimitPorCliente()
    {
        using var state = new DeploymentState();
        using var host = new DeploymentFactory(state);
        using var client = host.Client("http://localhost");
        var connection = await Send(client, HttpMethod.Get, "/test/connection", "127.0.0.1", "198.51.100.10", "https");
        Assert.Equal(HttpStatusCode.OK, connection.StatusCode);
        var info = await connection.Content.ReadFromJsonAsync<ConnectionInfo>();
        Assert.Equal("https", info!.Scheme);
        Assert.Equal("198.51.100.10", info.Ip);
        // /v1/auth/renovar tem limite próprio (15/min por IP), mais restrito que o geral de auth (30/min).
        for (var i = 0; i < 15; i++)
            Assert.Equal(HttpStatusCode.Unauthorized, (await Send(client, HttpMethod.Post, "/v1/auth/renovar", "127.0.0.1", "198.51.100.10", "https")).StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await Send(client, HttpMethod.Post, "/v1/auth/renovar", "127.0.0.1", "198.51.100.10", "https")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await Send(client, HttpMethod.Post, "/v1/auth/renovar", "127.0.0.1", "198.51.100.11", "https")).StatusCode);
    }

    [Fact]
    public async Task ProxyDesconhecido_NaoPodeForjarIpOuHttps()
    {
        using var state = new DeploymentState();
        using var host = new DeploymentFactory(state);
        using var https = host.Client();
        var response = await Send(https, HttpMethod.Get, "/test/connection", "203.0.113.25", "198.51.100.10", "http");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var info = await response.Content.ReadFromJsonAsync<ConnectionInfo>();
        Assert.Equal("203.0.113.25", info!.Ip);
        Assert.Equal("https", info.Scheme);
        using var http = host.Client("http://localhost");
        var redirected = await Send(http, HttpMethod.Get, "/test/connection", "203.0.113.25", "198.51.100.10", "https");
        Assert.Equal(HttpStatusCode.TemporaryRedirect, redirected.StatusCode);
        Assert.Equal("https", redirected.Headers.Location?.Scheme);
    }

    [Theory]
    [InlineData("domain-error", HttpStatusCode.Conflict, "VINCULO_DUPLICADO")]
    [InlineData("unexpected-error", HttpStatusCode.InternalServerError, "ERRO_INTERNO")]
    public async Task Erros_DeveMapearDominioEBugSemExporMensagem(string endpoint, HttpStatusCode status, string code)
    {
        using var state = new DeploymentState();
        using var host = new DeploymentFactory(state);
        using var client = host.Client();
        var response = await client.GetAsync("/test/" + endpoint);
        Assert.Equal(status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(code, body);
        Assert.DoesNotContain("SYNTHETIC_PRIVATE_DETAIL", body);
        Assert.Contains("traceId", body);
    }

    [Theory]
    [InlineData("Jwt:SigningKey")]
    [InlineData("DataProtection:KeysPath")]
    public void Production_SemConfiguracaoObrigatoria_DeveFalharClaramente(string missing)
    {
        using var state = new DeploymentState();
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:SigningKey"] = missing == "Jwt:SigningKey" ? null : state.SigningKey,
            ["DataProtection:KeysPath"] = missing == "DataProtection:KeysPath" ? null : state.KeysPath
        }).Build();
        var environment = new ProductionEnvironment();
        var error = missing == "Jwt:SigningKey"
            ? Assert.Throws<InvalidOperationException>(() => AuthenticationKeys.CreateJwt(config, environment))
            : Assert.Throws<InvalidOperationException>(() => AuthenticationKeys.CreateDataProtection(config, environment));
        Assert.Contains(missing, error.Message);
        Assert.DoesNotContain(state.SigningKey, error.Message);
    }

    [Theory]
    [InlineData("not-valid-base64")]
    [InlineData("c2hvcnQ=")]
    public void ChaveJwtInvalida_DeveFalharSemExporValor(string key)
    {
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:SigningKey"] = key }).Build();
        var error = Assert.Throws<InvalidOperationException>(() => AuthenticationKeys.CreateJwt(config, new ProductionEnvironment()));
        Assert.Contains("Jwt:SigningKey", error.Message);
        Assert.DoesNotContain(key, error.Message);
    }

    [Theory]
    [InlineData("confirmar_email", "hoodi://confirmar")]
    [InlineData("reset_senha", "hoodi://redefinir-senha")]
    public void Email_DeveConterDeepLinkComTokenSomenteNoFragmento(string purpose, string prefix)
    {
        const string syntheticToken = "SYNTHETIC_TOKEN_0123456789abcdef";
        var body = AuthenticationEmailBody.Create(purpose, syntheticToken);
        var link = new Uri(body.Split('\n', StringSplitOptions.RemoveEmptyEntries).Last());
        Assert.StartsWith(prefix, link.AbsoluteUri);
        Assert.Empty(link.Query);
        Assert.Equal("#token=" + syntheticToken, link.Fragment);
    }

    [Fact]
    public async Task DemoHttp_ExplicitamenteHabilitada_NaoDeveRedirecionarPara443()
    {
        using var state = new DeploymentState();
        using var host = new DeploymentFactory(state, redirectEnabled: false);
        using var client = host.Client("http://localhost");
        var response = await Send(client, HttpMethod.Get, "/test/connection", "127.0.0.1", "198.51.100.10", "http");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Equal("http", (await response.Content.ReadFromJsonAsync<ConnectionInfo>())!.Scheme);
        var health = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
        Assert.Null(health.Headers.Location);
    }

    [Fact]
    public async Task Https_PortaConfigurada_DeveSerUsadaNoRedirecionamento()
    {
        using var state = new DeploymentState();
        using var host = new DeploymentFactory(state, httpsPort: 8443);
        using var client = host.Client("http://localhost");
        var response = await Send(client, HttpMethod.Get, "/test/connection", "127.0.0.1", "198.51.100.10", "http");
        Assert.Equal(HttpStatusCode.TemporaryRedirect, response.StatusCode);
        Assert.Equal(8443, response.Headers.Location!.Port);
    }
    private static Task<HttpResponseMessage> Send(HttpClient client, HttpMethod method, string path, string remoteIp, string forwardedIp, string proto)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Add("X-Test-Remote", remoteIp);
        request.Headers.Add("X-Forwarded-For", forwardedIp);
        request.Headers.Add("X-Forwarded-Proto", proto);
        if (method == HttpMethod.Post) request.Content = JsonContent.Create(new { refreshToken = "SYNTHETIC_INVALID_REFRESH_TOKEN" });
        return client.SendAsync(request);
    }
}

public sealed record ConnectionInfo(string? Ip, string Scheme);

public sealed class DeploymentState : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "hoodi-deployment-test-" + Guid.NewGuid().ToString("N"));
    public string DatabasePath => Path.Combine(root, "test.db");
    public string KeysPath => Path.Combine(root, "keys");
    public string SigningKey { get; } = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
    public DeploymentState() => Directory.CreateDirectory(root);
    public void Dispose()
    {
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        foreach (var suffix in new[] { "", "-wal", "-shm" }) File.Delete(DatabasePath + suffix);
        if (Directory.Exists(KeysPath))
        {
            foreach (var file in Directory.EnumerateFiles(KeysPath)) File.Delete(file);
            Directory.Delete(KeysPath);
        }
        Directory.Delete(root);
    }
}

public sealed class DeploymentFactory(DeploymentState state, bool redirectEnabled = true, int httpsPort = 443) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Production");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Database:ConnectionString"] = "Data Source=" + state.DatabasePath,
            ["DataProtection:KeysPath"] = state.KeysPath,
            ["Jwt:SigningKey"] = state.SigningKey,
            ["Https:RedirectEnabled"] = redirectEnabled.ToString(),
            ["Https:Port"] = httpsPort.ToString()
        }));
        builder.ConfigureServices(services =>
        {
            // No SMTP side effects; this suite verifies persisted pending mail across host recreation.
            foreach (var worker in services.Where(x => x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(EmailOutboxWorker)).ToList())
                services.Remove(worker);
            services.AddControllers().AddApplicationPart(typeof(DeploymentProbeController).Assembly);
            services.AddSingleton<IStartupFilter, ConnectionStartupFilter>();
        });
    }
    public HttpClient Client(string address = "https://localhost") => CreateClient(new WebApplicationFactoryClientOptions
    { BaseAddress = new Uri(address), AllowAutoRedirect = false });
    public async Task<string> TokenAsync(string address)
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoodiDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>().CreateProtector("hoodi.auth.email-outbox.v1");
        return (await db.Emails.ToListAsync()).Select(x => JsonSerializer.Deserialize<EmailAutenticacao>(protector.Unprotect(x.ConteudoCifrado))!)
            .Single(x => x.Email == address).Token;
    }
}

public sealed class ConnectionStartupFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((context, nextMiddleware) =>
        {
            if (IPAddress.TryParse(context.Request.Headers["X-Test-Remote"].ToString(), out var ip)) context.Connection.RemoteIpAddress = ip;
            return nextMiddleware(context);
        });
        next(app);
    };
}

[ApiController, AllowAnonymous, Route("test")]
public sealed class DeploymentProbeController : ControllerBase
{
    [HttpGet("connection")]
    public ConnectionInfo Connection() => new(HttpContext.Connection.RemoteIpAddress?.ToString(), Request.Scheme);
    [HttpGet("domain-error")]
    public IActionResult DomainError() => throw new DomainException("VINCULO_DUPLICADO", "SYNTHETIC_PRIVATE_DETAIL");
    [HttpGet("unexpected-error")]
    public IActionResult UnexpectedError() => throw new InvalidOperationException("SYNTHETIC_PRIVATE_DETAIL");
}

public sealed class ProductionEnvironment : IHostEnvironment
{
    public string EnvironmentName { get; set; } = Environments.Production;
    public string ApplicationName { get; set; } = "Hoodi.Tests";
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = new Microsoft.Extensions.FileProviders.NullFileProvider();
}








