using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Hoodi.Application.Risk;

public sealed class SnapshotImportService(IProducerSnapshotDecoder decoder, IH3CellValidator h3, ISnapshotPublicationStore store)
{
    public async Task<PublicationResult> ImportAsync(SnapshotPackage input, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(input.Version) || input.Version.Length > 128 ||
            input.CsvBytes is not { Length: > 0 } || input.ManifestBytes is not { Length: > 0 } || input.FixtureBytes is not { Length: > 0 })
            throw new SnapshotValidationException("PACOTE_INVALIDO");
        // Own the buffers before any await; subsequent caller mutations cannot alter the validated package.
        var package = input with { CsvBytes = input.CsvBytes.ToArray(), ManifestBytes = input.ManifestBytes.ToArray(), FixtureBytes = input.FixtureBytes.ToArray() };
        var sha256 = Convert.ToHexString(SHA256.HashData(package.CsvBytes)).ToLowerInvariant();
        if (package.ExpectedSha256 is null || !Regex.IsMatch(package.ExpectedSha256, "^[0-9A-Fa-f]{64}$") ||
            !string.Equals(package.ExpectedSha256, sha256, StringComparison.OrdinalIgnoreCase))
            throw new SnapshotValidationException("CHECKSUM_INVALIDO");
        ValidateJson(package.ManifestBytes, "MANIFEST_JSON_INVALIDO");
        ValidateJson(package.FixtureBytes, "FIXTURE_JSON_INVALIDO");
        var decoded = await decoder.DecodeAsync(package, ct);
        ValidateCells(decoded);
        ct.ThrowIfCancellationRequested();
        return await store.PublishAsync(package, sha256, ct);
    }

    private void ValidateCells(DecodedSnapshot snapshot)
    {
        if (snapshot.ExpectedCoverage.Count == 0 || snapshot.Cells.Count != (long)snapshot.ExpectedCoverage.Count * 5)
            throw new SnapshotValidationException("COBERTURA_INCOMPLETA");
        foreach (var cell in snapshot.ExpectedCoverage) ValidateH3(cell);
        var keys = new HashSet<(string Cell, int Band)>();
        foreach (var row in snapshot.Cells)
        {
            ValidateH3(row.H3Cell);
            if (row.Band is < 0 or > 4) throw new SnapshotValidationException("FAIXA_INVALIDA");
            if (!snapshot.ExpectedCoverage.Contains(row.H3Cell)) throw new SnapshotValidationException("CELULA_FORA_COBERTURA");
            if (!keys.Add((row.H3Cell, row.Band))) throw new SnapshotValidationException("CHAVE_DUPLICADA");
            if (row.Status == "sem_dado")
            {
                // A blank score or explicit zero is not converted to a risk level.
                // The real producer adapter must settle any further representation constraints.
                if (row.Level is not null || row.RecordCount != 0 || (row.MonthlyScore is not null && row.MonthlyScore != 0))
                    throw new SnapshotValidationException("SEM_DADO_INCONSISTENTE");
            }
            else if (row.Status == "com_dado")
            {
                if (row.Level is null or < 0 or > 3 || row.RecordCount < 1 || row.MonthlyScore is null or <= 0)
                    throw new SnapshotValidationException("COM_DADO_INCONSISTENTE");
            }
            else throw new SnapshotValidationException("STATUS_INVALIDO");
        }
        foreach (var cell in snapshot.ExpectedCoverage)
            for (var band = 0; band < 5; band++)
                if (!keys.Contains((cell, band))) throw new SnapshotValidationException("COBERTURA_INCOMPLETA");
    }
    private void ValidateH3(string cell)
    {
        if (cell is null || !Regex.IsMatch(cell, "^[0-9a-f]{15}$") || !h3.IsValidResolution9(cell))
            throw new SnapshotValidationException("H3_INVALIDO");
    }
    private static void ValidateJson(byte[] bytes, string code)
    {
        try
        {
            using var document = JsonDocument.Parse(bytes);
            if (document.RootElement.ValueKind == JsonValueKind.Null) throw new SnapshotValidationException(code);
        }
        catch (JsonException) { throw new SnapshotValidationException(code); }
    }
}
