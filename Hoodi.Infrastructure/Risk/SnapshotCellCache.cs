using System.Collections.Frozen;
using Hoodi.Application.Risk;

namespace Hoodi.Infrastructure.Risk;

// One immutable current index per process. Scoped stores/decoders are only used during the call.
public sealed class SnapshotCellCache : IDisposable
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private CachedRiskIndex? current;

    public async Task<CachedCell?> LookupAsync(ISnapshotPublicationStore store, IProducerSnapshotDecoder decoder,
        string cell, int band, CancellationToken ct)
    {
        var index = await ReadIndexAsync(store, decoder, ct);
        return index is null ? null : Find(index, cell, band);
    }
    public async Task<CachedRiskIndex?> ReadIndexAsync(ISnapshotPublicationStore store, IProducerSnapshotDecoder decoder, CancellationToken ct)
    {
        var descriptor = await store.ReadCurrentDescriptorAsync(ct);
        if (descriptor is null) return null;
        var entry = Volatile.Read(ref current);
        if (entry?.Descriptor == descriptor) return entry;
        await gate.WaitAsync(ct);
        try
        {
            entry = Volatile.Read(ref current);
            if (entry?.Descriptor == descriptor) return entry;
            // The head may have changed while waiting. Decode the coherent current artifact,
            // never combine an old descriptor with bytes from another publication.
            var snapshot = await store.ReadCurrentAsync(ct);
            if (snapshot is null) return null;
            var actual = new SnapshotDescriptor(snapshot.Version, snapshot.Sha256, snapshot.PublishedAtUtc);
            if (entry?.Descriptor == actual) return entry;
            var decoded = await decoder.DecodeAsync(new(snapshot.Version, snapshot.Sha256,
                snapshot.CsvBytes, snapshot.ManifestBytes, snapshot.FixtureBytes), ct);
            ct.ThrowIfCancellationRequested();
            entry = new(actual, decoded.Cells.ToFrozenDictionary(x => (x.H3Cell, x.Band)));
            Volatile.Write(ref current, entry);
            return entry;
        }
        finally { gate.Release(); }
    }
    private static CachedCell Find(CachedRiskIndex entry, string cell, int band) =>
        new(entry.Descriptor, entry.Rows.GetValueOrDefault((cell, band)));
    public void Dispose() => gate.Dispose();
}
public sealed record CachedCell(SnapshotDescriptor Publication, RiskCell? Row);

public sealed record CachedRiskIndex(SnapshotDescriptor Descriptor, FrozenDictionary<(string Cell, int Band), RiskCell> Rows);
