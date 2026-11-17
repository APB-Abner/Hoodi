using System.ComponentModel.DataAnnotations;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hoodi.Application.Errors;
using Hoodi.Application.Risk;
using Hoodi.Infrastructure.Risk;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace Hoodi.Api.Controllers;

[ApiController, AllowAnonymous, Route("v1/risco")]
[ProducesResponseType<ProblemDetails>(404)]
public sealed class GradeController(ISnapshotPublicationStore store, IProducerSnapshotDecoder decoder, IH3CellValidator h3) : ControllerBase
{
    [HttpGet("versao-atual")]
    [ProducesResponseType<GradeMetadata>(200)]
    public async Task<ActionResult<GradeMetadata>> Metadata(CancellationToken ct)
    {
        var s = await Current(ct);
        using var document = JsonDocument.Parse(s.ManifestBytes);
        return Ok(new GradeMetadata(s.Version, s.Sha256, s.PublishedAtUtc, document.RootElement.Clone()));
    }
    [HttpGet("grade")]
    [Produces("text/csv")]
    [ProducesResponseType(200)]
    [ProducesResponseType(304)]
    public async Task<IActionResult> Snapshot(CancellationToken ct)
    {
        var s = await Current(ct); var etag = '"' + s.Sha256 + '"'; Response.Headers.ETag = etag;
        if (Request.Headers.IfNoneMatch.Any(value => value?.Split(',').Select(x => x.Trim()).Contains(etag) == true)) return StatusCode(304);
        return File(s.CsvBytes, "text/csv; charset=utf-8", "risco_h3_faixa_v2.csv");
    }
    [HttpGet("celulas/{h3Cell}")]
    [ProducesResponseType<GradeCell>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    public async Task<ActionResult<GradeCell>> Cell(string h3Cell, [FromQuery, Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired, Range(0, 4)] int faixa, CancellationToken ct)
    {
        if (!h3.IsValidResolution9(h3Cell) || h3Cell.Any(char.IsUpper)) throw new RequestException("H3_INVALIDO", 400);
        var s = await Current(ct);
        var decoded = await decoder.DecodeAsync(new(s.Version, s.Sha256, s.CsvBytes, s.ManifestBytes, s.FixtureBytes), ct);
        var row = decoded.Cells.SingleOrDefault(x => x.H3Cell == h3Cell && x.Band == faixa);
        return Ok(new GradeCell(s.Version, s.Sha256, h3Cell, faixa, row?.Status ?? "fora_cobertura", row?.Level, row?.MonthlyScore, row?.RecordCount));
    }
    private async Task<PublishedSnapshot> Current(CancellationToken ct) => await store.ReadCurrentAsync(ct) ?? throw new RequestException("SNAPSHOT_INDISPONIVEL", 404);
}
public sealed record GradeMetadata(string Versao, string Sha256, DateTimeOffset PublicadoEm, JsonElement Manifest);
public sealed record GradeCell(string Versao, string Sha256, string H3Cell, int Faixa, string Status, int? Nivel, decimal? PontuacaoMensal, int? NRegistros);

[ApiController, Authorize, Route("v1/admin/risco/importar")]
public sealed class GradeImportController(SnapshotImportService importer, IOptions<RiskSnapshotOptions> options) : ControllerBase
{
    [HttpPost, Consumes("multipart/form-data"), RequestSizeLimit(1048576)]
    [ProducesResponseType<PublicationResult>(201)]
    [ProducesResponseType<PublicationResult>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(403)]
    [ProducesResponseType<ProblemDetails>(409)]
    [ProducesResponseType<ProblemDetails>(413)]
    [ProducesResponseType<ProblemDetails>(422)]
    public async Task<ActionResult<PublicationResult>> Import([FromForm] SnapshotUpload input, CancellationToken ct)
    {
        var user = Guid.Parse(User.FindFirst("sub")!.Value);
        if (!options.Value.ImporterUserIds.Contains(user)) throw new RequestException("IMPORTACAO_NAO_AUTORIZADA", 403);
        var csv = await Read(input.Csv, 900000, ct); var manifest = await Read(input.Manifest, 64000, ct);
        var fixture = await Read(input.Fixture, 64000, ct); var checksum = Encoding.UTF8.GetString(await Read(input.Sha256, 256, ct)).Trim();
        var parts = checksum.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 2 || parts[1] != "risco_h3_faixa_v2.csv") throw new RequestException("CHECKSUM_ARQUIVO_INVALIDO", 400);
        if (Hash(csv) != options.Value.ApprovedCsvSha256 || Hash(manifest) != options.Value.ApprovedManifestSha256 || Hash(fixture) != options.Value.ApprovedFixtureSha256)
            throw new SnapshotValidationException("ARTEFATO_NAO_APROVADO");
        var result = await importer.ImportAsync(new(input.Versao, parts[0], csv, manifest, fixture), ct);
        return StatusCode(result.AlreadyPublished ? 200 : 201, result);
    }
    private static async Task<byte[]> Read(IFormFile file, int limit, CancellationToken ct)
    {
        if (file.Length <= 0 || file.Length > limit) throw new RequestException("ARQUIVO_TAMANHO_INVALIDO", 413);
        await using var stream = file.OpenReadStream(); using var output = new MemoryStream();
        await stream.CopyToAsync(output, ct);
        if (output.Length > limit) throw new RequestException("ARQUIVO_TAMANHO_INVALIDO", 413);
        return output.ToArray();
    }
    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
public sealed class SnapshotUpload
{
    [Required, StringLength(128, MinimumLength = 1)] public string Versao { get; set; } = "";
    [Required] public IFormFile Csv { get; set; } = null!;
    [Required] public IFormFile Sha256 { get; set; } = null!;
    [Required] public IFormFile Manifest { get; set; } = null!;
    [Required] public IFormFile Fixture { get; set; } = null!;
}
