using System.Net;
using System.Net.Http.Json;
using System.Text;
using Hoodi.Application.Errors;
using Hoodi.Application.Lugares;
using Hoodi.Application.Risk;
using Hoodi.Application.Rotas;
using Hoodi.Infrastructure.Rotas;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hoodi.Tests;

public sealed class LugaresTests
{
    // Hora fixa 14:00 em São Paulo = faixa 2 (tarde).
    private static readonly DateTimeOffset Agora = DateTimeOffset.Parse("2026-11-18T14:00:00-03:00");
    private static readonly PontoRota Aqui = new(-23.5614, -46.6559);

    private static LugaresService Servico(Fakes.Provedor p, bool grade = true) =>
        new(p, new Fakes.Grade(grade), new RouteScoring(new Fakes.Espacial()), new Fakes.Relogio(Agora));

    [Theory]
    [InlineData("")]
    [InlineData("ab")]
    [InlineData("  a ")]
    public async Task Sugerir_TextoCurtoNaoChamaOProvedor(string texto)
    {
        var p = new Fakes.Provedor();
        var erro = await Assert.ThrowsAsync<RequestException>(() => Servico(p).SugerirAsync(new(texto), default));
        Assert.Equal(400, erro.Status); Assert.Equal(0, p.Chamadas);
    }

    [Fact]
    public async Task Sugerir_RejeitaTextoLongoOuComControle()
    {
        var p = new Fakes.Provedor();
        await Assert.ThrowsAsync<RequestException>(() => Servico(p).SugerirAsync(new(new string('a', 81)), default));
        await Assert.ThrowsAsync<RequestException>(() => Servico(p).SugerirAsync(new("Paulis\nta"), default));
        Assert.Equal(0, p.Chamadas);
    }

    [Fact]
    public async Task Sugerir_LimitaACincoRemoveRepetidosEDescartaCoordenadaInvalida()
    {
        var p = new Fakes.Provedor { Achados = [.. Enumerable.Range(0, 9).Select(i => Lugar("a" + i, -23.56 - i * 0.001, -46.65)),
            Lugar("a0", -23.5, -46.6), Lugar("x", 999, 0)] };
        var r = await Servico(p).SugerirAsync(new("Paulista"), default);
        Assert.Equal("ok", r.Situacao); Assert.Equal(5, r.Sugestoes.Count); Assert.Equal(5, r.Sugestoes.Select(s => s.Id).Distinct().Count());
        Assert.DoesNotContain(r.Sugestoes, s => s.Id == "x");
    }

    [Fact]
    public async Task Sugerir_ProvedorForaDoArNaoVira_Erro()
    {
        var r = await Servico(new Fakes.Provedor { Falha = true }).SugerirAsync(new("Paulista"), default);
        Assert.Equal("indisponivel", r.Situacao); Assert.Empty(r.Sugestoes);
    }

    [Fact]
    public async Task Recomendar_OrdenaPorRiscoDepoisDistancia_SemDadoNuncaAntesDeComDado()
    {
        var p = new Fakes.Provedor { Achados = [
            Lugar("sem", -23.5700, -46.6559),       // célula sem dado
            Lugar("alto", -23.5620, -46.6559),      // nível 3, mais perto de todos
            Lugar("calmo-longe", -23.5650, -46.6559), // nível 0, mais longe
            Lugar("calmo-perto", -23.5630, -46.6559), // nível 0, mais perto
            Lugar("fora", -23.5680, -46.6559),      // fora da cobertura
        ] };
        var r = await Servico(p).RecomendarAsync(new(Aqui, "farmacia"), default);
        Assert.Equal("ok", r.Situacao); Assert.Equal(2, r.Faixa);
        Assert.Equal(["calmo-perto", "calmo-longe", "alto", "sem", "fora"], r.Lugares.Select(l => l.Id).ToArray());
        Assert.All(r.Lugares.Where(l => l.Situacao != "com_dado"), l => Assert.Null(l.Nivel));
        Assert.Equal("farmácia", p.UltimoTermo);
    }

    [Fact]
    public async Task Recomendar_ArredondaAPosicaoEnviadaAoProvedorEIgnoraQuemEstaForaDoRaio()
    {
        var p = new Fakes.Provedor { Achados = [Lugar("perto", -23.5620, -46.6559), Lugar("longe", -23.5800, -46.6559)] };
        var r = await Servico(p).RecomendarAsync(new(new(-23.56144, -46.65591), "mercado"), default);
        Assert.Equal(new PontoRota(-23.561, -46.656), p.UltimoCentro);
        Assert.Equal(["perto"], r.Lugares.Select(l => l.Id).ToArray());
    }

    [Theory]
    [InlineData("pizza")]
    [InlineData("")]
    public async Task Recomendar_CategoriaForaDaListaNaoChamaOProvedor(string categoria)
    {
        var p = new Fakes.Provedor();
        await Assert.ThrowsAsync<RequestException>(() => Servico(p).RecomendarAsync(new(Aqui, categoria), default));
        Assert.Equal(0, p.Chamadas);
    }

    [Fact]
    public async Task Recomendar_MotivosHonestos()
    {
        var p = new Fakes.Provedor();
        Assert.Equal("grade_indisponivel", (await Servico(p, grade: false).RecomendarAsync(new(Aqui, "cafe"), default)).Motivo);
        Assert.Equal("fora_territorio", (await Servico(p).RecomendarAsync(new(new(-22.9, -43.2), "cafe"), default)).Motivo);
        Assert.Equal(0, p.Chamadas);
        p.Falha = true;
        Assert.Equal("provedor_indisponivel", (await Servico(p).RecomendarAsync(new(Aqui, "cafe"), default)).Motivo);
        p.Falha = false; p.Achados = [];
        var vazio = await Servico(p).RecomendarAsync(new(Aqui, "cafe"), default);
        Assert.Equal("ok", vazio.Situacao); Assert.Equal("nenhum_lugar", vazio.Motivo);
    }

    [Fact]
    public async Task Provedor_UsaChaveNoCabecalhoRegiaoFixaENaoEnviaOTextoEmLog()
    {
        var h = new Fakes.Http("""{"features":[{"geometry":{"type":"Point","coordinates":[-46.6559,-23.5614]},"properties":{"gid":"openstreetmap:venue:1","name":"Farmácia Boa","label":"Farmácia Boa, Bela Vista, São Paulo, Brazil","neighbourhood":"Bela Vista"}},{"geometry":{"type":"Point","coordinates":[1,2]},"properties":{}}]}""");
        var provedor = new HeigitPlacesProvider(new HttpClient(h), Options.Create(new RotasOptions { ApiKey = "SYNTHETIC_ONLY" }));
        var r = await provedor.SugerirAsync("Av. Paulista 1106", default);
        var unico = Assert.Single(r);
        Assert.Equal("Farmácia Boa, Bela Vista, São Paulo, Brazil", unico.Nome); Assert.Equal("Bela Vista", unico.Contexto);
        Assert.Equal(-23.5614, unico.Coordenada.Latitude, 4);
        Assert.StartsWith("https://api.heigit.org/openrouteservice/geocode/autocomplete?", h.Url);
        Assert.Contains("boundary.rect.min_lon=-46.83", h.Url); Assert.Contains("boundary.country=BR", h.Url);
        Assert.DoesNotContain("api_key", h.Url!); Assert.Equal("SYNTHETIC_ONLY", h.Chave);
    }

    [Theory]
    [InlineData("503")]
    [InlineData("malformed")]
    [InlineData("timeout")]
    public async Task Provedor_FalhasViramProvedorLugaresException(string cenario)
    {
        var provedor = new HeigitPlacesProvider(new HttpClient(new Fakes.Http("{}", cenario)), Options.Create(new RotasOptions { ApiKey = "SYNTHETIC_ONLY" }));
        await Assert.ThrowsAsync<ProvedorLugaresException>(() => provedor.SugerirAsync("Paulista", default));
    }

    [Fact]
    public async Task Provedor_SemChaveNaoFazRequisicao()
    {
        var h = new Fakes.Http("{}");
        await Assert.ThrowsAsync<ProvedorLugaresException>(() => new HeigitPlacesProvider(new HttpClient(h), Options.Create(new RotasOptions())).SugerirAsync("Paulista", default));
        Assert.Null(h.Url);
    }

    [Fact]
    public async Task Provedor_BuscaPerto_EnviaPosicaoComTresCasasERaioEmKm()
    {
        var h = new Fakes.Http("""{"features":[]}""");
        await new HeigitPlacesProvider(new HttpClient(h), Options.Create(new RotasOptions { ApiKey = "K" })).BuscarPertoAsync("farmácia", new(-23.56144, -46.65591), 1500, default);
        Assert.Contains("boundary.circle.lat=-23.561&", h.Url); Assert.Contains("boundary.circle.lon=-46.656&", h.Url); Assert.Contains("boundary.circle.radius=1.5&", h.Url);
        Assert.Contains("layers=venue", h.Url);
    }

    [Fact]
    public async Task Endpoint_ExigeLoginRejeitaCamposDesconhecidosETextoCurto()
    {
        using var factory = new AuthFactory(); using var anon = factory.Client();
        using var configured = factory.WithWebHostBuilder(b => b.ConfigureServices(s =>
            s.AddScoped<IProvedorLugares>(_ => new Fakes.Provedor { Achados = [Lugar("a", -23.5614, -46.6559)] })));
        using var app = configured.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        using var semLogin = configured.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.Unauthorized, (await semLogin.PostAsJsonAsync("/v1/lugares/sugerir", new { texto = "Paulista" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await semLogin.PostAsJsonAsync("/v1/lugares/recomendar", new { posicao = new { latitude = -23.56, longitude = -46.65 }, categoria = "cafe" })).StatusCode);
        await PanicoAndContaIntegrationTests.Seed(factory, app, "lugares-user");
        var ok = await app.PostAsJsonAsync("/v1/lugares/sugerir", new { texto = "Paulista" });
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        Assert.Single((await ok.Content.ReadFromJsonAsync<SugestoesLugares>())!.Sugestoes);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.PostAsJsonAsync("/v1/lugares/sugerir", new { texto = "ab" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.PostAsJsonAsync("/v1/lugares/sugerir", new { texto = "Paulista", extra = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await app.PostAsJsonAsync("/v1/lugares/recomendar", new { posicao = new { latitude = -23.56, longitude = -46.65 }, categoria = "pizza" })).StatusCode);
    }

    private static LugarEncontrado Lugar(string id, double lat, double lon) => new(id, "Lugar " + id, "Bela Vista", new(lat, lon));

    private static class Fakes
    {
        // Célula = latitude arredondada a 3 casas (≈111 m), com a longitude fixa: facilita montar a grade nos testes.
        public sealed class Espacial : IIndiceEspacialRotas { public string Cell(PontoRota p) => Math.Round(p.Latitude, 3).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture); }
        public sealed class Relogio(DateTimeOffset agora) : TimeProvider { public override DateTimeOffset GetUtcNow() => agora.ToUniversalTime(); }
        public sealed class Grade(bool disponivel) : IGradeParaRotas
        {
            public Task<GradeParaRotas?> LerAtualAsync(CancellationToken ct)
            {
                if (!disponivel) return Task.FromResult<GradeParaRotas?>(null);
                var rows = new Dictionary<(string, int), RiskCell>();
                void Linha(double lat, string status, int? nivel) { var c = lat.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture); rows[(c, 2)] = new(c, 2, status, nivel, null, 3); }
                Linha(-23.561, "com_dado", 1);   // posição do usuário
                Linha(-23.562, "com_dado", 3);   // alto
                Linha(-23.563, "com_dado", 0);   // calmo perto
                Linha(-23.565, "com_dado", 0);   // calmo longe
                Linha(-23.570, "sem_dado", null);
                return Task.FromResult<GradeParaRotas?>(new("v3-teste", rows));
            }
        }
        public sealed class Provedor : IProvedorLugares
        {
            public IReadOnlyList<LugarEncontrado> Achados = []; public bool Falha; public int Chamadas; public string? UltimoTermo; public PontoRota? UltimoCentro;
            public Task<IReadOnlyList<LugarEncontrado>> SugerirAsync(string texto, CancellationToken ct) { Chamadas++; return Falha ? throw new ProvedorLugaresException() : Task.FromResult(Achados); }
            public Task<IReadOnlyList<LugarEncontrado>> BuscarPertoAsync(string termo, PontoRota centro, int raio, CancellationToken ct)
            { Chamadas++; UltimoTermo = termo; UltimoCentro = centro; return Falha ? throw new ProvedorLugaresException() : Task.FromResult(Achados); }
        }
        public sealed class Http(string corpo, string cenario = "ok") : HttpMessageHandler
        {
            public string? Url; public string? Chave;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                Url = request.RequestUri!.AbsoluteUri; Chave = request.Headers.TryGetValues("Authorization", out var v) ? v.Single() : null;
                if (cenario == "timeout") throw new TaskCanceledException();
                return Task.FromResult(new HttpResponseMessage(cenario == "503" ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
                { Content = new StringContent(cenario == "malformed" ? "<html/>" : corpo, Encoding.UTF8, "application/json") });
            }
        }
    }
}
