using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Hoodi.Api.Controllers;
using Hoodi.Application.Risk;
using Hoodi.Application.Rotas;
using Hoodi.Infrastructure.Rotas;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hoodi.Tests;

public sealed class RouteHttpTests
{
    [Fact]
    public async Task MissingKeyMustNotMakeAnyProviderRequest()
    {
        var handler = new ProviderStandIn("success");
        var gateway = new HeigitRoutesProvider(new HttpClient(handler), Options.Create(new RotasOptions()));
        await Assert.ThrowsAsync<ProvedorRotasException>(() => gateway.ObterAsync(new(1, 2), new(2, 3), default));
        Assert.Null(handler.Url);
    }
    [Theory]
    [InlineData("success")]
    [InlineData("503")]
    [InlineData("timeout")]
    [InlineData("malformed")]
    public async Task GatewayMustUsePrivateHeaderLonLatAndBoundedWalkingAlternatives(string scenario)
    {
        var handler = new ProviderStandIn(scenario);
        var gateway = new HeigitRoutesProvider(new HttpClient(handler), Options.Create(new RotasOptions { ApiKey = "SYNTHETIC_ONLY" }));
        if (scenario == "success") Assert.Equal(2, (await gateway.ObterAsync(new(-23.56, -46.63), new(-23.55, -46.64), default)).Count);
        else await Assert.ThrowsAsync<ProvedorRotasException>(() => gateway.ObterAsync(new(-23.56, -46.63), new(-23.55, -46.64), default));
        Assert.Equal("https://api.heigit.org/openrouteservice/v2/directions/foot-walking/geojson", handler.Url);
        Assert.True(handler.PrivateHeaderPresent);
        using var body = JsonDocument.Parse(handler.Body!);
        Assert.Equal(-46.63, body.RootElement.GetProperty("coordinates")[0][0].GetDouble());
        Assert.Equal(-23.56, body.RootElement.GetProperty("coordinates")[0][1].GetDouble());
        Assert.Equal(3, body.RootElement.GetProperty("alternative_routes").GetProperty("target_count").GetInt32());
    }
    [Fact]
    public async Task EndpointMustEvaluateConfiguredModesAndRejectInvalidTimeWithoutPersistingRoute()
    {
        using var factory = new AuthFactory(); using var client = factory.Client();
        var handler = new ProviderStandIn("success");
        // The real HTTP adapter runs against a synthetic boundary, never the live provider.
        using var configured = factory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddScoped<IProvedorRotas>(sp => new HeigitRoutesProvider(new HttpClient(handler), Options.Create(new RotasOptions { ApiKey = "SYNTHETIC_ONLY" })))));
        using var app = configured.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        // Shared factory database and signing configuration; login through the configured host.
        await PanicoAndContaIntegrationTests.Seed(factory, app, "routes-user");
        using (var scope = configured.Services.CreateScope())
        {
            var path = Path.Combine(AppContext.BaseDirectory, "RiskFixture");
            await scope.ServiceProvider.GetRequiredService<SnapshotImportService>().ImportAsync(new("route-v2", Hoodi.Infrastructure.Risk.ProducerV2Decoder.DeliveredSha256,
                File.ReadAllBytes(Path.Combine(path, "risco_h3_faixa_v2.csv")), File.ReadAllBytes(Path.Combine(path, "manifest.json")), File.ReadAllBytes(Path.Combine(path, "fixture_v2.json"))));
        }
        var input = new { origem = new { latitude = -23.55937804586215, longitude = -46.62905519645476 }, destino = new { latitude = -23.55770344834097, longitude = -46.63206653567886 }, saidaEm = factory.Clock.GetUtcNow().ToString("O") };
        using (var scope = configured.Services.CreateScope())
        { var options = scope.ServiceProvider.GetRequiredService<IOptions<RotasOptions>>().Value; options.FonteRisco = FonteRisco.Nivel; options.SemDado = TratamentoSemDado.RiscoBaixo; }
        var response = await app.PostAsJsonAsync("/v1/rotas/avaliar", input); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<AvaliacaoRotas>())!;
        Assert.Equal("Nivel", result.FonteRisco); Assert.Equal("RiscoBaixo", result.SemDado); Assert.Equal("route-v2", result.VersaoGrade);
        Assert.Equal(2, result.Rotas.Count); Assert.All(result.Rotas, r => Assert.NotEmpty(r.Geometria));
        Assert.Equal("nivel_x_km", result.UnidadeIndice);
        handler.Scenario = "503";
        var unavailable = await app.PostAsJsonAsync("/v1/rotas/avaliar", input);
        Assert.Equal(HttpStatusCode.OK, unavailable.StatusCode);
        var failed = (await unavailable.Content.ReadFromJsonAsync<AvaliacaoRotas>())!;
        Assert.Equal("provedor_indisponivel", failed.Motivo); Assert.Empty(failed.Rotas);
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await app.PostAsJsonAsync("/v1/rotas/avaliar", new { input.origem, input.destino, saidaEm = factory.Clock.GetUtcNow().AddHours(49).ToString("O") })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.PostAsJsonAsync("/v1/rotas/avaliar", new { input.origem, input.destino, saidaEm = "2026-11-17T10:00:00" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.PostAsJsonAsync("/v1/rotas/avaliar", new { origem = new { }, input.destino, input.saidaEm })).StatusCode);
        using var anonymous = configured.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/v1/rotas/avaliar", input)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await app.GetAsync("/openapi/v1.json")).StatusCode);
    }
    private sealed class ProviderStandIn(string scenario) : HttpMessageHandler
    {
        public string Scenario = scenario;
        public string? Url; public string? Body; public bool PrivateHeaderPresent;
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Url = request.RequestUri!.AbsoluteUri; Body = await request.Content!.ReadAsStringAsync(ct);
            PrivateHeaderPresent = request.Headers.TryGetValues("Authorization", out var values) && values.Single() == "SYNTHETIC_ONLY";
            if (Scenario == "timeout") throw new TaskCanceledException();
            var json = """
            {"features":[{"geometry":{"type":"LineString","coordinates":[[-46.62905519645476,-23.55937804586215],[-46.63206653567886,-23.55770344834097]]},"properties":{"summary":{"duration":300}}},{"geometry":{"type":"LineString","coordinates":[[-46.62905519645476,-23.55937804586215],[-46.6305,-23.558],[-46.63206653567886,-23.55770344834097]]},"properties":{"summary":{"duration":350}}}]}
            """;
            return new(Scenario == "503" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            { Content = new StringContent(Scenario == "malformed" ? "<html>error</html>" : json, Encoding.UTF8, "application/json") };
        }
    }
}
