using System.Security.Cryptography;
using System.Text;
using Hoodi.Application.Risk;
using Hoodi.Infrastructure.Persistence;
using Hoodi.Infrastructure.Risk;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Hoodi.Tests;

// Synthetic decoder/cell whitelist only. These tests do not substitute for producer artifacts or real H3 validation.
public sealed class SnapshotImportTests
{
    private const string A = "8928308280fffff";
    private const string B = "8928308280bffff";
    private const string Outside = "89283082873ffff";

    [Fact]
    public async Task Publication_MustBeDurableIdempotentAndNeverRewindOnOldReplay()
    {
        using var state = new DeploymentState();
        var options = Options(state);
        await using (var db = new HoodiDbContext(options))
        {
            await db.Database.MigrateAsync();
            var decoder = new SyntheticDecoder();
            var store = new SnapshotPublicationStore(db, TimeProvider.System);
            var service = new SnapshotImportService(decoder, new SyntheticH3(), store);
            Assert.Null(await store.ReadCurrentAsync(default));
            var first = await service.ImportAsync(Package("v1"));
            Assert.False(first.AlreadyPublished);
            Assert.True((await service.ImportAsync(Package("v1"))).AlreadyPublished);
            var second = await service.ImportAsync(Package("v2"));
            Assert.False(second.AlreadyPublished);
            Assert.True((await service.ImportAsync(Package("v1"))).AlreadyPublished);
            Assert.Equal("v2", (await store.ReadCurrentAsync(default))!.Version);
            Assert.Equal(2, await db.SnapshotPublications.CountAsync());
            var conflicting = Package("v2") with { FixtureBytes = "[1]"u8.ToArray() };
            var error = await Assert.ThrowsAsync<SnapshotValidationException>(() => service.ImportAsync(conflicting));
            Assert.Equal("VERSAO_CONFLITANTE", error.Code);
            Assert.Equal("v2", (await store.ReadCurrentAsync(default))!.Version);
        }
        await using var restarted = new HoodiDbContext(options);
        var current = await new SnapshotPublicationStore(restarted, TimeProvider.System).ReadCurrentAsync(default);
        Assert.Equal("v2", current!.Version);
        Assert.Equal(Package("v2").CsvBytes, current.CsvBytes);
    }

    [Fact]
    public async Task Hash_MustUseExactBytesBeforeDecodingAndPreservePreviousPublication()
    {
        using var state = new DeploymentState();
        await using var db = new HoodiDbContext(Options(state));
        await db.Database.MigrateAsync();
        var decoder = new SyntheticDecoder();
        var store = new SnapshotPublicationStore(db, TimeProvider.System);
        var service = new SnapshotImportService(decoder, new SyntheticH3(), store);
        await service.ImportAsync(Package("v1"));
        decoder.Calls = 0;
        var candidate = Package("v2");
        candidate = candidate with { CsvBytes = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(candidate.CsvBytes).Replace("\n", "\r\n")) };
        var error = await Assert.ThrowsAsync<SnapshotValidationException>(() => service.ImportAsync(candidate));
        Assert.Equal("CHECKSUM_INVALIDO", error.Code);
        Assert.Equal(0, decoder.Calls);
        Assert.Equal("v1", (await store.ReadCurrentAsync(default))!.Version);
        Assert.False(await db.SnapshotPublications.AnyAsync(x => x.Version == "v2"));
    }

    [Theory]
    [InlineData("duplicate", "CHAVE_DUPLICADA")]
    [InlineData("missing-band", "COBERTURA_INCOMPLETA")]
    [InlineData("missing-cell", "COBERTURA_INCOMPLETA")]
    [InlineData("outside", "CELULA_FORA_COBERTURA")]
    [InlineData("h3", "H3_INVALIDO")]
    [InlineData("band", "FAIXA_INVALIDA")]
    [InlineData("status", "STATUS_INVALIDO")]
    [InlineData("unknown-zero", "SEM_DADO_INCONSISTENTE")]
    [InlineData("no-record", "COM_DADO_INCONSISTENTE")]
    [InlineData("level-four", "COM_DADO_INCONSISTENTE")]
    [InlineData("manifest-json", "MANIFEST_JSON_INVALIDO")]
    [InlineData("fixture-json", "FIXTURE_JSON_INVALIDO")]
    public async Task InvalidCandidate_MustNotReplaceTheCurrentVersion(string kind, string expected)
    {
        using var state = new DeploymentState();
        await using var db = new HoodiDbContext(Options(state));
        await db.Database.MigrateAsync();
        var decoder = new SyntheticDecoder();
        var store = new SnapshotPublicationStore(db, TimeProvider.System);
        var service = new SnapshotImportService(decoder, new SyntheticH3(), store);
        await service.ImportAsync(Package("v1"));
        var rows = ValidRows();
        switch (kind)
        {
            case "duplicate": rows[1] = rows[0]; break;
            case "missing-band": rows.RemoveAt(0); break;
            case "missing-cell": rows.RemoveAll(x => x.H3Cell == B); break;
            case "outside": rows[0] = rows[0] with { H3Cell = Outside }; break;
            case "h3": rows[0] = rows[0] with { H3Cell = "8828308280fffff" }; break;
            case "band": rows[0] = rows[0] with { Band = 5 }; break;
            case "status": rows[0] = rows[0] with { Status = "unknown" }; break;
            case "unknown-zero": rows[1] = rows[1] with { Level = 0 }; break;
            case "no-record": rows[0] = rows[0] with { RecordCount = 0 }; break;
            case "level-four": rows[0] = rows[0] with { Level = 4 }; break;
        }
        decoder.Value = new DecodedSnapshot(rows, new HashSet<string> { A, B });
        var candidate = Package("v2");
        if (kind == "manifest-json") candidate = candidate with { ManifestBytes = "{"u8.ToArray() };
        if (kind == "fixture-json") candidate = candidate with { FixtureBytes = "["u8.ToArray() };
        var error = await Assert.ThrowsAsync<SnapshotValidationException>(() => service.ImportAsync(candidate));
        Assert.Equal(expected, error.Code);
        Assert.Equal("v1", (await store.ReadCurrentAsync(default))!.Version);
        Assert.Single(await db.SnapshotPublications.ToListAsync());
    }

    [Fact]
    public async Task FailureAfterSqlWrites_MustRollBackCandidateAndPointerTogether()
    {
        using var state = new DeploymentState();
        var options = Options(state);
        await using var goodDb = new HoodiDbContext(options);
        await goodDb.Database.MigrateAsync();
        var goodStore = new SnapshotPublicationStore(goodDb, TimeProvider.System);
        await new SnapshotImportService(new SyntheticDecoder(), new SyntheticH3(), goodStore).ImportAsync(Package("v1"));
        var failingOptions = new DbContextOptionsBuilder<HoodiDbContext>(options).AddInterceptors(new FailAfterSave()).Options;
        await using (var failingDb = new HoodiDbContext(failingOptions))
        {
            var service = new SnapshotImportService(new SyntheticDecoder(), new SyntheticH3(), new SnapshotPublicationStore(failingDb, TimeProvider.System));
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ImportAsync(Package("v2")));
        }
        Assert.Equal("v1", (await goodStore.ReadCurrentAsync(default))!.Version);
        Assert.False(await goodDb.SnapshotPublications.AnyAsync(x => x.Version == "v2"));
        await new SnapshotImportService(new SyntheticDecoder(), new SyntheticH3(), goodStore).ImportAsync(Package("v2"));
        Assert.Equal("v2", (await goodStore.ReadCurrentAsync(default))!.Version);
    }

    [Fact]
    public async Task ConcurrentSameVersion_MustPublishOnce()
    {
        using var state = new DeploymentState();
        var options = Options(state);
        await using var first = new HoodiDbContext(options);
        await first.Database.MigrateAsync();
        await using var second = new HoodiDbContext(options);
        var a = new SnapshotImportService(new SyntheticDecoder(), new SyntheticH3(), new SnapshotPublicationStore(first, TimeProvider.System));
        var b = new SnapshotImportService(new SyntheticDecoder(), new SyntheticH3(), new SnapshotPublicationStore(second, TimeProvider.System));
        var results = await Task.WhenAll(a.ImportAsync(Package("v1")), b.ImportAsync(Package("v1")));
        Assert.Single(results, x => !x.AlreadyPublished);
        Assert.Single(results, x => x.AlreadyPublished);
        Assert.Single(await first.SnapshotPublications.AsNoTracking().ToListAsync());
    }

    private static DbContextOptions<HoodiDbContext> Options(DeploymentState state) =>
        new DbContextOptionsBuilder<HoodiDbContext>().UseSqlite("Data Source=" + state.DatabasePath).Options;
    private static SnapshotPackage Package(string version)
    {
        // Not producer CSV/manifest/fixture; bytes are synthetic pipeline-control inputs.
        var bytes = Encoding.UTF8.GetBytes("synthetic snapshot " + version + "\n");
        return new SnapshotPackage(version, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(), bytes, "{}"u8.ToArray(), "[]"u8.ToArray());
    }
    private static List<RiskCell> ValidRows() => new[] { A, B }.SelectMany(cell => Enumerable.Range(0, 5)
        .Select(band => band == 1 ? new RiskCell(cell, band, "sem_dado", null, 0, 0) : new RiskCell(cell, band, "com_dado", 0, 1, 1))).ToList();
    private sealed class SyntheticDecoder : IProducerSnapshotDecoder
    {
        public int Calls;
        public DecodedSnapshot Value { get; set; } = new(ValidRows(), new HashSet<string> { A, B });
        public Task<DecodedSnapshot> DecodeAsync(SnapshotPackage package, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult(Value);
        }
    }
    private sealed class SyntheticH3 : IH3CellValidator
    {
        public bool IsValidResolution9(string cell) => cell is A or B or Outside;
    }
    private sealed class FailAfterSave : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("SYNTHETIC_WRITE_FAILURE_AFTER_SQL");
    }
}
