using System.Security.Cryptography;
using System.Text.Json;
using Hoodi.Application.Errors;
using Hoodi.Application.Risk;
using Hoodi.Application.Rotas;
using Hoodi.Infrastructure.Risk;
using Hoodi.Infrastructure.Rotas;

namespace Hoodi.Tests;

public sealed class RouteScoringTests
{
    private static string Fixture => Path.Combine(AppContext.BaseDirectory, "RouteFixture", "vetores_rota_v2.json");
    public static IEnumerable<object[]> Cases()
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Fixture));
        return document.RootElement.GetProperty("casos").EnumerateArray().Concat(document.RootElement.GetProperty("rotas_fronteira").EnumerateArray())
            .Select(c => new object[] { c.GetProperty("nome").GetString()! }).ToArray();
    }
    [Theory, MemberData(nameof(Cases))]
    public async Task MustMatchProducerGoldenRoute(string name)
    {
        using var document = JsonDocument.Parse(File.ReadAllBytes(Fixture)); var root = document.RootElement;
        var sample = root.GetProperty("casos").EnumerateArray().Concat(root.GetProperty("rotas_fronteira").EnumerateArray()).Single(x => x.GetProperty("nome").GetString() == name);
        var folder = Path.Combine(AppContext.BaseDirectory, "RiskFixture");
        var package = new SnapshotPackage("v2", ProducerV2Decoder.DeliveredSha256, File.ReadAllBytes(Path.Combine(folder, "risco_h3_faixa_v2.csv")), File.ReadAllBytes(Path.Combine(folder, "manifest.json")), File.ReadAllBytes(Path.Combine(folder, "fixture_v2.json")));
        var snapshot = (await new ProducerV2Decoder(new H3SpatialIndex()).DecodeAsync(package, default)).Cells.ToDictionary(x => (x.H3Cell, x.Band));
        var engine = new RouteScoring(new H3SpatialIndex()); var scores = new List<PontuacaoRota>();
        foreach (var route in sample.GetProperty("rotas").EnumerateArray())
        {
            var input = route.GetProperty("entrada"); var points = input.GetProperty("pontos").EnumerateArray().Select(p => new PontoRota(p[0].GetDouble(), p[1].GetDouble())).ToArray();
            if (route.TryGetProperty("erro", out _)) { Assert.Throws<RequestException>(() => engine.Slice(points)); continue; }
            var segments = engine.Slice(points); var expectedCells = route.GetProperty("celulas").EnumerateArray().ToArray();
            Assert.Equal(expectedCells.Select(c => c.GetProperty("h3_cell").GetString()), segments.Select(s => s.H3Cell));
            for (var i = 0; i < segments.Count; i++) Assert.InRange(Math.Abs(segments[i].Metros - expectedCells[i].GetProperty("metros").GetDouble()), 0, .01);
            var score = engine.Score(segments, input.GetProperty("faixa").GetInt32(), snapshot, FonteRisco.PontuacaoMensal, TratamentoSemDado.Desconhecido); scores.Add(score);
            var expected = route.GetProperty("resultado");
            Close(expected.GetProperty("pontuacao_acumulada_observada").GetDouble(), score.Acumulada);
            if (expected.GetProperty("indice_por_km").ValueKind == JsonValueKind.Null) Assert.Null(score.IndicePorKm);
            else Close(expected.GetProperty("indice_por_km").GetDouble(), score.IndicePorKm!.Value);
            Close(expected.GetProperty("km_total").GetDouble(), score.MetrosTotal / 1000);
            Close(expected.GetProperty("km_com_dado").GetDouble(), score.MetrosComDado / 1000);
            Close(expected.GetProperty("pct_sem_dado").GetDouble(), score.PercentualSemDado);
            Close(expected.GetProperty("pct_fora_da_cobertura").GetDouble(), score.PercentualForaCobertura);
            Assert.Equal(expected.GetProperty("dado_insuficiente").GetBoolean(), score.DadoInsuficiente);
        }
        if (sample.TryGetProperty("ordenacao_posicoes", out var positions))
            Assert.Equal(positions.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.Null ? (int?)null : x.GetInt32()), engine.Rank(scores));
    }
    [Fact]
    public void MustMatchAll40ProducerH3PointsAndTieVectors()
    {
        var bytes = File.ReadAllBytes(Fixture);
        Assert.Equal("93b1980b88eecae77c744bc4b78084b3d6eb26ae3484908d5c077d4b656fac84", Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
        using var doc = JsonDocument.Parse(bytes);
        var points = doc.RootElement.GetProperty("pontos_interiores").EnumerateArray().Concat(doc.RootElement.GetProperty("pontos_fronteira").EnumerateArray()).ToArray();
        Assert.Equal(40, points.Length);
        foreach (var point in points) Assert.Equal(point.GetProperty("h3_cell").GetString(), new H3SpatialIndex().Cell(new(point.GetProperty("lat").GetDouble(), point.GetProperty("lon").GetDouble())));
        foreach (var tie in doc.RootElement.GetProperty("empate_vetores").EnumerateArray()) Assert.Equal(tie.GetProperty("empate").GetBoolean(), RouteScoring.Tie(tie.GetProperty("a").GetDouble(), tie.GetProperty("b").GetDouble()));
    }
    [Theory]
    [InlineData(FonteRisco.PontuacaoMensal, TratamentoSemDado.Desconhecido, 4, 50)]
    [InlineData(FonteRisco.PontuacaoMensal, TratamentoSemDado.RiscoBaixo, 4, 80)]
    [InlineData(FonteRisco.Nivel, TratamentoSemDado.Desconhecido, 1.5, 50)]
    [InlineData(FonteRisco.Nivel, TratamentoSemDado.RiscoBaixo, 1.5, 80)]
    public void ModesMustChangeScoreAndEffectiveCoverageWithoutHidingMissingData(FonteRisco source, TratamentoSemDado missing, double expected, double coverage)
    {
        var snapshot = new Dictionary<(string, int), RiskCell> { [("known", 2)] = new("known", 2, "com_dado", 3, 8, 20), [("no-data", 2)] = new("no-data", 2, "sem_dado", null, null, 0) };
        var result = new RouteScoring(new H3SpatialIndex()).Score([new("known", 500), new("no-data", 300), new("outside", 200)], 2, snapshot, source, missing);
        Close(expected, result.Acumulada); Close(coverage, result.CoberturaPercentual); Close(30, result.PercentualSemDado); Close(20, result.PercentualForaCobertura);
        Assert.Equal(missing == TratamentoSemDado.Desconhecido, result.DadoInsuficiente);
    }
    [Fact]
    public void RankingMustUseAnchoredClassesAndUnknownMustNeverGainRank()
    {
        PontuacaoRota Make(double value, double meters = 1000, bool insufficient = false) => new(value, value, meters, meters, 0, 0, 100, insufficient);
        var result = new RouteScoring(new H3SpatialIndex()).Rank([Make(1), Make(1 + .75e-9), Make(1 + 1.5e-9), Make(0, insufficient: true)]);
        Assert.Equal(new int?[] { 1, 2, 3, null }, result);
        Assert.Equal(new int?[] { 2, 1 }, new RouteScoring(new H3SpatialIndex()).Rank([Make(1, 2000), Make(1 + .5e-9, 1000)]));
    }
    private static void Close(double expected, double actual) => Assert.InRange(Math.Abs(expected - actual), 0, Math.Max(1, Math.Abs(expected)) * 1e-6);
}
