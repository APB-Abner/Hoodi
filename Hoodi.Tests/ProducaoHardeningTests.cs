using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Hoodi.Api.Auth;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Hoodi.Tests;

/// <summary>Endurecimento para produção: cabeçalhos, documentação opcional, CORS fechado e limite da renovação.</summary>
public sealed class ProducaoHardeningTests
{
    private sealed class HardeningFactory(DeploymentState state, Dictionary<string, string?>? extra = null) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Production");
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:ConnectionString"] = "Data Source=" + state.DatabasePath,
                    ["DataProtection:KeysPath"] = state.KeysPath,
                    ["Jwt:SigningKey"] = state.SigningKey,
                    ["Https:RedirectEnabled"] = "false"
                });
                if (extra is not null) config.AddInMemoryCollection(extra);
            });
            builder.ConfigureServices(services =>
            {
                foreach (var worker in services.Where(x => x.ServiceType == typeof(IHostedService) && x.ImplementationType == typeof(EmailOutboxWorker)).ToList())
                    services.Remove(worker);
            });
        }
    }

    [Fact]
    public async Task Respostas_DevemTrazerCabecalhosDeSeguranca()
    {
        using var state = new DeploymentState();
        using var host = new HardeningFactory(state);
        var r = await host.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("nosniff", r.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-referrer", r.Headers.GetValues("Referrer-Policy").Single());
        Assert.Equal("DENY", r.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("no-store", r.Headers.CacheControl?.ToString());
    }

    [Fact]
    public async Task Docs_DevemPoderSerDesligadasPorConfiguracao()
    {
        using var state = new DeploymentState();
        using var host = new HardeningFactory(state, new() { ["Docs:Enabled"] = "false" });
        var client = host.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/openapi/v1.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/scalar/")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/health")).StatusCode);
    }

    [Fact]
    public async Task VersaoDoHealth_DeveSerAMesmaDoOpenApi()
    {
        using var state = new DeploymentState();
        using var host = new HardeningFactory(state);
        var client = host.CreateClient();
        using var health = JsonDocument.Parse(await client.GetStringAsync("/health"));
        using var openapi = JsonDocument.Parse(await client.GetStringAsync("/openapi/v1.json"));
        Assert.Equal(openapi.RootElement.GetProperty("info").GetProperty("version").GetString(),
            health.RootElement.GetProperty("versaoApi").GetString());
    }

    [Fact]
    public async Task Cors_DeveSerFechadoPorPadraoEAbrirSoParaOrigemConfigurada()
    {
        using var closedState = new DeploymentState();
        using var closed = new HardeningFactory(closedState);
        var pedido = new HttpRequestMessage(HttpMethod.Get, "/health");
        pedido.Headers.Add("Origin", "https://app.exemplo.invalid");
        Assert.False((await closed.CreateClient().SendAsync(pedido)).Headers.Contains("Access-Control-Allow-Origin"));

        using var openState = new DeploymentState();
        using var open = new HardeningFactory(openState, new() { ["Cors:AllowedOrigins:0"] = "https://app.exemplo.invalid" });
        var permitido = new HttpRequestMessage(HttpMethod.Get, "/health");
        permitido.Headers.Add("Origin", "https://app.exemplo.invalid");
        var resposta = await open.CreateClient().SendAsync(permitido);
        Assert.Equal("https://app.exemplo.invalid", resposta.Headers.GetValues("Access-Control-Allow-Origin").Single());
        var outro = new HttpRequestMessage(HttpMethod.Get, "/health");
        outro.Headers.Add("Origin", "https://malicioso.invalid");
        Assert.False((await open.CreateClient().SendAsync(outro)).Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Renovar_DeveTerLimiteMaisRestritoQueOGeralDeAuth()
    {
        using var state = new DeploymentState();
        using var host = new HardeningFactory(state);
        var client = host.CreateClient();
        var codigos = new List<HttpStatusCode>();
        for (var i = 0; i < 17; i++)
            codigos.Add((await client.PostAsJsonAsync("/v1/auth/renovar", new { refreshToken = "invalido" })).StatusCode);
        Assert.DoesNotContain(HttpStatusCode.TooManyRequests, codigos.Take(15));
        Assert.Equal(HttpStatusCode.TooManyRequests, codigos[15]);
    }
}
