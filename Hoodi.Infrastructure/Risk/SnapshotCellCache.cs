using System.Collections.Frozen;
using Hoodi.Application.Risk;

namespace Hoodi.Infrastructure.Risk;

// One immutable current index per process. Scoped stores/decoders are only used during the call.
public sealed class SnapshotCellCache : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private Entry? current;

    public async Task<CachedCell?> LookupAsync(ISnapshotPublicationStore store, IProducerSnapshotDecoder decoder,
        string cell, int band, CancellationToken ct)
    {
        var descriptor = await store.ReadCurrentDescriptorAsync(ct);
        if (descriptor is null) return null;
        var entry = Volatile.Read(ref current);
        if (entry?.Descriptor == descriptor) return Find(entry, cell, band);
        await gate.WaitAsync(ct);
        try
        {
            entry = Volatile.Read(ref current);
            if (entry?.Descriptor == descriptor) return Find(entry, cell, band);
            // The head may have changed while waiting. Decode the coherent current artifact,
            // never combine an old descriptor with bytes from another publication.
            var snapshot = await store.ReadCurrentAsync(ct);
            if (snapshot is null) return null;
            var actual = new SnapshotDescriptor(snapshot.Version, snapshot.Sha256, snapshot.PublishedAtUtc);
            if (entry?.Descriptor == actual) return Find(entry, cell, band);
            var decoded = await decoder.DecodeAsync(new(snapshot.Version, snapshot.Sha256,
                snapshot.CsvBytes, snapshot.ManifestBytes, snapshot.FixtureBytes), ct);
            ct.ThrowIfCancellationRequested();
            entry = new(actual, decoded.Cells.ToFrozenDictionary(x => (x.H3Cell, x.Band)));
            Volatile.Write(ref current, entry);
            return Find(entry, cell, band);
        }
        finally { gate.Release(); }
    }
    private static CachedCell Find(Entry entry, string cell, int band) =>
        new(entry.Descriptor, entry.Rows.GetValueOrDefault((cell, band)));
    public void Dispose() => gate.Dispose();
    private sealed record Entry(SnapshotDescriptor Descriptor, FrozenDictionary<(string Cell, int Band), RiskCell> Rows);
}
public sealed record CachedCell(SnapshotDescriptor Publication, RiskCell? Row);
