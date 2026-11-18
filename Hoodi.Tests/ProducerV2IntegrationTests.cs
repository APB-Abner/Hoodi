using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Hoodi.Api.Controllers;
using Hoodi.Application.Risk;
using Hoodi.Infrastructure.Persistence;
using Hoodi.Infrastructure.Risk;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Hoodi.Tests;

public sealed class ProducerV2IntegrationTests
{
    [Theory]
    [InlineData(2, 954, 441)]
    [InlineData(3, 824, 571)]
    public async Task RealProducerPackage_MustValidateExactOverlapCoverageAndAll1395Rows(int contract, int withData, int noData)
    {
        var spatial = new H3SpatialIndex(); var decoder = new ProducerV2Decoder(spatial);
        var snapshot = await decoder.DecodeAsync(Package(contract), default);
        Assert.Equal(279, snapshot.ExpectedCoverage.Count); Assert.Equal(1395, snapshot.Cells.Count);
        Assert.Equal(withData, snapshot.Cells.Count(x => x.Status == "com_dado"));
        Assert.Equal(noData, snapshot.Cells.Count(x => x.Status == "sem_dado"));
        Assert.All(snapshot.Cells.Where(x => x.Status == "com_dado"), x => Assert.True(x.RecordCount >= 3));
        Assert.All(snapshot.Cells.Where(x => x.Status == "sem_dado"), x => { Assert.Null(x.Level); Assert.Null(x.MonthlyScore); Assert.Equal(0, x.RecordCount); });
        Assert.True(spatial.IsValidResolution9("89a8100c00bffff"));
        Assert.False(spatial.IsValidResolution9("89fffffffffffff"));
        Assert.False(spatial.IsValidResolution9("88a8100c01fffff"));
    }

    [Theory]
    [InlineData(2, "abr–set", "VERSAO_OU_FORMULA")]
    [InlineData(3, "jan–set", "VERSAO_OU_FORMULA")]
    public async Task RealDecoder_MustRejectWindowOrFormulaFromAnotherContractVersion(int contract, string otherWindow, string _)
    {
        var package = Package(contract); var manifest = JsonNode.Parse(package.ManifestBytes)!;
        var own = contract == 2 ? "jan–set" : "abr–set";
        manifest["formula_pontuacao"] = manifest["formula_pontuacao"]!.GetValue<string>().Replace(own, otherWindow);
        package = package with { ManifestBytes = Encoding.UTF8.GetBytes(manifest.ToJsonString()) };
        await Assert.ThrowsAsync<SnapshotValidationException>(() => new ProducerV2Decoder(new H3SpatialIndex()).DecodeAsync(package, default));
        var unknown = JsonNode.Parse(Package(contract).ManifestBytes)!; unknown["contrato_versao"] = 4;
        await Assert.ThrowsAsync<SnapshotValidationException>(() => new ProducerV2Decoder(new H3SpatialIndex())
            .DecodeAsync(Package(contract) with { ManifestBytes = Encoding.UTF8.GetBytes(unknown.ToJsonString()) }, default));
    }

    [Theory]
    [InlineData("privacy")]
    [InlineData("fixture")]
    [InlineData("band")]
    [InlineData("perimeter")]
    [InlineData("duplicate")]
    [InlineData("missing-cell")]
    [InlineData("count-leak")]
    [InlineData("score")]
    [InlineData("crlf")]
    public async Task RealDecoder_MustRejectTamperedProducerArtifacts(string kind)
    {
        var package = Package(); var manifest = JsonNode.Parse(package.ManifestBytes)!;
        var fixture = JsonNode.Parse(package.FixtureBytes)!;
        if (kind == "privacy") manifest["privacidade"]!["k_minimo"] = 1;
        if (kind == "band") manifest["faixas"]![3]!["intervalo_horas"] = "[18,24)";
        if (kind == "perimeter") manifest["perimetro"]!["lat_min"] = -23.58;
        if (kind == "fixture") fixture["chaves"]![1]!["nivel"] = 0;
        var lines = Encoding.UTF8.GetString(package.CsvBytes).Split('\n').ToList();
        if (kind == "duplicate") lines[2] = lines[1];
        if (kind == "missing-cell") { lines.RemoveRange(1, 5); manifest["n_chaves"] = 1390; }
        if (kind is "count-leak" or "score")
        {
            var index = lines.FindIndex(x => x.Contains(",com_dado,")); var parts = lines[index].Split(',');
            parts[kind == "count-leak" ? 6 : 5] = kind == "count-leak" ? "2" : "1000000";
            lines[index] = string.Join(',', parts);
        }
        var csv = Encoding.UTF8.GetBytes(string.Join(kind == "crlf" ? "\r\n" : "\n", lines));
        var sha = Convert.ToHexString(SHA256.HashData(csv)).ToLowerInvariant(); manifest["csv_sha256"] = sha;
        package = package with { CsvBytes = csv, ExpectedSha256 = sha, ManifestBytes = Encoding.UTF8.GetBytes(manifest.ToJsonString()), FixtureBytes = Encoding.UTF8.GetBytes(fixture.ToJsonString()) };
        await Assert.ThrowsAsync<SnapshotValidationException>(() => new ProducerV2Decoder(new H3SpatialIndex()).DecodeAsync(package, default));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task HttpImport_MustRequireOperatorAndPublishAtomically_WithExplicitOutsideCoverage(int contract)
    {
        var sha = contract == 2 ? ProducerV2Decoder.DeliveredSha256 : ProducerV2Decoder.DeliveredSha256V3;
        using var factory = new AuthFactory(); using var op = factory.Client(); using var publicClient = factory.Client();
        Assert.Equal(HttpStatusCode.NotFound, (await publicClient.GetAsync("/v1/risco/grade")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await publicClient.PostAsync("/v1/admin/risco/importar", Upload(contract))).StatusCode);
        var user = await PanicoAndContaIntegrationTests.Seed(factory, op, "operator");
        Assert.Equal(HttpStatusCode.Forbidden, (await op.PostAsync("/v1/admin/risco/importar", Upload(contract))).StatusCode);
        using (var scope = factory.Services.CreateScope())
        {
            var risk = scope.ServiceProvider.GetRequiredService<IOptions<RiskSnapshotOptions>>().Value; risk.ImporterUserIds = [user];
            // Defaults are the v3 release; v2 is the rollback release and must be approved explicitly (Risk__Approved*).
            if (contract == 2) { risk.ApprovedCsvSha256 = ProducerV2Decoder.DeliveredSha256; risk.ApprovedManifestSha256 = Sha(Bytes(2, "manifest.json")); risk.ApprovedFixtureSha256 = Sha(Bytes(2, "fixture_v2.json")); }
        }
        var imported = await op.PostAsync("/v1/admin/risco/importar", Upload(contract));
        Assert.Equal(HttpStatusCode.Created, imported.StatusCode);
        Assert.False((await imported.Content.ReadFromJsonAsync<PublicationResult>())!.AlreadyPublished);
        Assert.True((await (await op.PostAsync("/v1/admin/risco/importar", Upload(contract))).Content.ReadFromJsonAsync<PublicationResult>())!.AlreadyPublished);
        var bytes = await publicClient.GetByteArrayAsync("/v1/risco/grade"); Assert.Equal(Package(contract).CsvBytes, bytes);
        var metadata = (await publicClient.GetFromJsonAsync<GradeMetadata>("/v1/risco/versao-atual"))!; Assert.Equal(sha, metadata.Sha256);
        var empty = (await publicClient.GetFromJsonAsync<GradeCell>("/v1/risco/celulas/89a8100c00bffff?faixa=0"))!;
        Assert.Equal("sem_dado", empty.Status); Assert.Null(empty.Nivel); Assert.Equal(0, empty.NRegistros);
        var outside = (await publicClient.GetFromJsonAsync<GradeCell>("/v1/risco/celulas/8928308280fffff?faixa=0"))!;
        Assert.Equal("fora_cobertura", outside.Status); Assert.Null(outside.Nivel); Assert.Null(outside.NRegistros);
        var known = (await publicClient.GetFromJsonAsync<GradeCell>("/v1/risco/celulas/89a8100c033ffff?faixa=2"))!;
        Assert.Equal("com_dado", known.Status); Assert.Equal(3, known.Nivel);
        Assert.Equal(HttpStatusCode.BadRequest, (await publicClient.GetAsync("/v1/risco/celulas/89fffffffffffff?faixa=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await publicClient.GetAsync("/v1/risco/celulas/89a8100c033ffff")).StatusCode);
        var rejected = await op.PostAsync("/v1/admin/risco/importar", Upload(contract, tamper: true)); Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        Assert.Equal(bytes, await publicClient.GetByteArrayAsync("/v1/risco/grade"));
        using var check = factory.Services.CreateScope(); Assert.Single(await check.ServiceProvider.GetRequiredService<HoodiDbContext>().SnapshotPublications.ToListAsync());
        using var etagRequest = new HttpRequestMessage(HttpMethod.Get, "/v1/risco/grade");
        etagRequest.Headers.IfNoneMatch.ParseAdd('"' + sha + '"');
        Assert.Equal(HttpStatusCode.NotModified, (await publicClient.SendAsync(etagRequest)).StatusCode);
    }
    private static SnapshotPackage Package(int contract = 2) => new(contract == 2 ? "nando-v2-2026-jan-set" : "nando-v3-2026-abr-set",
        contract == 2 ? ProducerV2Decoder.DeliveredSha256 : ProducerV2Decoder.DeliveredSha256V3,
        Bytes(contract, $"risco_h3_faixa_v{contract}.csv"), Bytes(contract, "manifest.json"), Bytes(contract, $"fixture_v{contract}.json"));
    private static byte[] Bytes(int contract, string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, contract == 2 ? "RiskFixture" : "RiskFixtureV3", name));
    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    private static MultipartFormDataContent Upload(int contract = 2, bool tamper = false)
    {
        var content = new MultipartFormDataContent(); content.Add(new StringContent(contract == 2 ? "nando-v2-2026-jan-set" : "nando-v3-2026-abr-set"), "versao");
        foreach (var (field, name) in new[] { ("csv", $"risco_h3_faixa_v{contract}.csv"), ("sha256", $"risco_h3_faixa_v{contract}.csv.sha256"), ("manifest", "manifest.json"), ("fixture", $"fixture_v{contract}.json") })
        {
            var bytes = Bytes(contract, name); if (tamper && field == "csv") bytes[0] ^= 1;
            content.Add(new ByteArrayContent(bytes), field, name);
        }
        return content;
    }
}
