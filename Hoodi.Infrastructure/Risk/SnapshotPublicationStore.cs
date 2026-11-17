using Hoodi.Application.Risk;
using Hoodi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hoodi.Infrastructure.Risk;

public sealed class SnapshotPublicationStore(HoodiDbContext db, TimeProvider clock) : ISnapshotPublicationStore
{
    public async Task<PublicationResult> PublishAsync(SnapshotPackage package, string sha256, CancellationToken ct)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(ct);
        try
        {
            var existing = await db.SnapshotPublications.AsNoTracking().SingleOrDefaultAsync(x => x.Version == package.Version, ct);
            if (existing is not null)
            {
                if (existing.Sha256 != sha256 || !existing.CsvBytes.AsSpan().SequenceEqual(package.CsvBytes) ||
                    !existing.ManifestBytes.AsSpan().SequenceEqual(package.ManifestBytes) || !existing.FixtureBytes.AsSpan().SequenceEqual(package.FixtureBytes))
                    throw new SnapshotValidationException("VERSAO_CONFLITANTE");
                await transaction.CommitAsync(ct);
                // Re-importing an older existing version never rewinds the current pointer.
                return new PublicationResult(existing.Version, existing.Sha256, existing.PublishedAtUtc, true);
            }
            var record = new SnapshotArtifact
            {
                Version = package.Version, Sha256 = sha256, PublishedAtUtc = clock.GetUtcNow(),
                CsvBytes = package.CsvBytes.ToArray(), ManifestBytes = package.ManifestBytes.ToArray(), FixtureBytes = package.FixtureBytes.ToArray()
            };
            db.SnapshotPublications.Add(record);
            var head = await db.SnapshotHeads.SingleOrDefaultAsync(x => x.Id == 1, ct);
            if (head is null) db.SnapshotHeads.Add(new SnapshotHead { Id = 1, Version = record.Version });
            else head.Version = record.Version;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return new PublicationResult(record.Version, record.Sha256, record.PublishedAtUtc, false);
        }
        catch
        {
            // Disposal rolls the outer transaction back, including failures after SQL writes.
            db.ChangeTracker.Clear();
            throw;
        }
    }

    public Task<SnapshotDescriptor?> ReadCurrentDescriptorAsync(CancellationToken ct) =>
        (from head in db.SnapshotHeads.AsNoTracking()
         where head.Id == 1
         join snapshot in db.SnapshotPublications.AsNoTracking() on head.Version equals snapshot.Version
         select new SnapshotDescriptor(snapshot.Version, snapshot.Sha256, snapshot.PublishedAtUtc)).SingleOrDefaultAsync(ct);

    public async Task<PublishedSnapshot?> ReadCurrentAsync(CancellationToken ct)
    {
        var record = await (from head in db.SnapshotHeads.AsNoTracking()
            where head.Id == 1
            join snapshot in db.SnapshotPublications.AsNoTracking() on head.Version equals snapshot.Version
            select snapshot).SingleOrDefaultAsync(ct);
        return record is null ? null : new PublishedSnapshot(record.Version, record.Sha256, record.PublishedAtUtc,
            record.CsvBytes, record.ManifestBytes, record.FixtureBytes);
    }
}

public sealed class SnapshotArtifact
{
    public string Version { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public DateTimeOffset PublishedAtUtc { get; set; }
    public byte[] CsvBytes { get; set; } = [];
    public byte[] ManifestBytes { get; set; } = [];
    public byte[] FixtureBytes { get; set; } = [];
}
public sealed class SnapshotHead
{
    public int Id { get; set; }
    public string Version { get; set; } = string.Empty;
}
