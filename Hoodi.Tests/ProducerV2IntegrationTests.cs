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
    [Fact]
    public async Task RealProducerPackage_MustValidateExactOverlapCoverageAndAll1395Rows()
    {
        var spatial = new H3SpatialIndex(); var decoder = new ProducerV2Decoder(spatial);
        var snapshot = await decoder.DecodeAsync(Package(), default);
        Assert.Equal(279, snapshot.ExpectedCoverage.Count); Assert.Equal(1395, snapshot.Cells.Count);
        Assert.Equal(954, snapshot.Cells.Count(x => x.Status == "com_dado"));
        Assert.Equal(441, snapshot.Cells.Count(x => x.Status == "sem_dado"));
        Assert.All(snapshot.Cells.Where(x => x.Status == "com_dado"), x => Assert.True(x.RecordCount >= 3));
        Assert.All(snapshot.Cells.Where(x => x.Status == "sem_dado"), x => { Assert.Null(x.Level); Assert.Null(x.MonthlyScore); Assert.Equal(0, x.RecordCount); });
        Assert.True(spatial.IsValidResolution9("89a8100c00bffff"));
        Assert.False(spatial.IsValidResolution9("89fffffffffffff"));
        Assert.False(spatial.IsValidResolution9("88a8100c01fffff"));
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

    [Fact]
    public async Task HttpImport_MustRequireOperatorAndPublishAtomically_WithExplicitOutsideCoverage()
    {
        using var factory = new AuthFactory(); using var op = factory.Client(); using var publicClient = factory.Client();
        Assert.Equal(HttpStatusCode.NotFound, (await publicClient.GetAsync("/v1/risco/grade")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await publicClient.PostAsync("/v1/admin/risco/importar", Upload())).StatusCode);
        var user = await PanicoAndContaIntegrationTests.Seed(factory, op, "operator");
        Assert.Equal(HttpStatusCode.Forbidden, (await op.PostAsync("/v1/admin/risco/importar", Upload())).StatusCode);
        using (var scope = factory.Services.CreateScope()) scope.ServiceProvider.GetRequiredService<IOptions<RiskSnapshotOptions>>().Value.ImporterUserIds = [user];
        var imported = await op.PostAsync("/v1/admin/risco/importar", Upload());
        Assert.Equal(HttpStatusCode.Created, imported.StatusCode);
        Assert.False((await imported.Content.ReadFromJsonAsync<PublicationResult>())!.AlreadyPublished);
        Assert.True((await (await op.PostAsync("/v1/admin/risco/importar", Upload())).Content.ReadFromJsonAsync<PublicationResult>())!.AlreadyPublished);
        var bytes = await publicClient.GetByteArrayAsync("/v1/risco/grade"); Assert.Equal(Package().CsvBytes, bytes);
        var metadata = (await publicClient.GetFromJsonAsync<GradeMetadata>("/v1/risco/versao-atual"))!; Assert.Equal(ProducerV2Decoder.DeliveredSha256, metadata.Sha256);
        var empty = (await publicClient.GetFromJsonAsync<GradeCell>("/v1/risco/celulas/89a8100c00bffff?faixa=0"))!;
        Assert.Equal("sem_dado", empty.Status); Assert.Null(empty.Nivel); Assert.Equal(0, empty.NRegistros);
        var outside = (await publicClient.GetFromJsonAsync<GradeCell>("/v1/risco/celulas/8928308280fffff?faixa=0"))!;
        Assert.Equal("fora_cobertura", outside.Status); Assert.Null(outside.Nivel); Assert.Null(outside.NRegistros);
        var known = (await publicClient.GetFromJsonAsync<GradeCell>("/v1/risco/celulas/89a8100c033ffff?faixa=2"))!;
        Assert.Equal("com_dado", known.Status); Assert.Equal(3, known.Nivel);
        Assert.Equal(HttpStatusCode.BadRequest, (await publicClient.GetAsync("/v1/risco/celulas/89fffffffffffff?faixa=0")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await publicClient.GetAsync("/v1/risco/celulas/89a8100c033ffff")).StatusCode);
        var rejected = await op.PostAsync("/v1/admin/risco/importar", Upload(tamper: true)); Assert.Equal(HttpStatusCode.UnprocessableEntity, rejected.StatusCode);
        Assert.Equal(bytes, await publicClient.GetByteArrayAsync("/v1/risco/grade"));
        using var check = factory.Services.CreateScope(); Assert.Single(await check.ServiceProvider.GetRequiredService<HoodiDbContext>().SnapshotPublications.ToListAsync());
        using var etagRequest = new HttpRequestMessage(HttpMethod.Get, "/v1/risco/grade");
        etagRequest.Headers.IfNoneMatch.ParseAdd('"' + ProducerV2Decoder.DeliveredSha256 + '"');
        Assert.Equal(HttpStatusCode.NotModified, (await publicClient.SendAsync(etagRequest)).StatusCode);
    }
    private static SnapshotPackage Package() => new("nando-v2-2026-jan-set", ProducerV2Decoder.DeliveredSha256,
        Bytes("risco_h3_faixa_v2.csv"), Bytes("manifest.json"), Bytes("fixture_v2.json"));
    private static byte[] Bytes(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "RiskFixture", name));
    private static MultipartFormDataContent Upload(bool tamper = false)
    {
        var content = new MultipartFormDataContent(); content.Add(new StringContent("nando-v2-2026-jan-set"), "versao");
        foreach (var (field, name) in new[] { ("csv", "risco_h3_faixa_v2.csv"), ("sha256", "risco_h3_faixa_v2.csv.sha256"), ("manifest", "manifest.json"), ("fixture", "fixture_v2.json") })
        {
            var bytes = Bytes(name); if (tamper && field == "csv") bytes[0] ^= 1;
            content.Add(new ByteArrayContent(bytes), field, name);
        }
        return content;
    }
}
