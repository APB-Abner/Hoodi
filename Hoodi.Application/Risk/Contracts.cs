namespace Hoodi.Application.Risk;

// Internal integration models, not producer JSON/CSV DTOs or public HTTP contracts.
public sealed record SnapshotPackage(string Version, string ExpectedSha256, byte[] CsvBytes, byte[] ManifestBytes, byte[] FixtureBytes);
public sealed record RiskCell(string H3Cell, int Band, string Status, int? Level, decimal? MonthlyScore, int RecordCount);
public sealed record DecodedSnapshot(IReadOnlyList<RiskCell> Cells, IReadOnlySet<string> ExpectedCoverage);
public sealed record PublishedSnapshot(string Version, string Sha256, DateTimeOffset PublishedAtUtc, byte[] CsvBytes, byte[] ManifestBytes, byte[] FixtureBytes);
public sealed record PublicationResult(string Version, string Sha256, DateTimeOffset PublishedAtUtc, bool AlreadyPublished);

public interface IProducerSnapshotDecoder
{
    // The real adapter must validate the delivered schemas, training cuts, time bands,
    // source period/formula and exact expected cell set/perimeter before returning.
    Task<DecodedSnapshot> DecodeAsync(SnapshotPackage package, CancellationToken ct);
}
public interface IH3CellValidator
{
    // Must use actual H3 cell/mode/resolution validation, not a regex alone.
    bool IsValidResolution9(string cell);
}
public interface ISnapshotPublicationStore
{
    Task<PublicationResult> PublishAsync(SnapshotPackage package, string sha256, CancellationToken ct);
    Task<PublishedSnapshot?> ReadCurrentAsync(CancellationToken ct);
}
public sealed class SnapshotValidationException(string code) : Exception("Snapshot validation failed.")
{
    public string Code { get; } = code;
}
