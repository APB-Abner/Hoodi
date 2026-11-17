using System.Data.Common;
using System.Net;
using Hoodi.Application.Risk;
using Hoodi.Infrastructure.Persistence;
using Hoodi.Infrastructure.Risk;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Hoodi.Tests;

public sealed class RiskReadCacheTests
{
    private const string Cell = "89a8100c033ffff";

    [Fact]
    public async Task WarmLookup_MustReadOnlySqlDescriptor_NotArtifactBlobsOrDecodeAgain()
    {
        using var state = new DeploymentState(); var commands = new ReadCommands();
        var options = new DbContextOptionsBuilder<HoodiDbContext>().UseSqlite("Data Source=" + state.DatabasePath).AddInterceptors(commands).Options;
        await using var db = new HoodiDbContext(options); await db.Database.MigrateAsync();
        var store = new SnapshotPublicationStore(db, TimeProvider.System);
        // Synthetic bytes: this verifies the lookup/storage boundary, not producer decoding.
        await store.PublishAsync(new("v1", new string('a', 64), [1], [2], [3]), new string('a', 64), default);
        var decoder = new CountingDecoder(); using var cache = new SnapshotCellCache();
        var first = await cache.LookupAsync(store, decoder, Cell, 2, default); Assert.Equal(1, first!.Row!.Level);
        commands.Text.Clear();
        for (var i = 0; i < 3; i++) Assert.Equal(first, await cache.LookupAsync(store, decoder, Cell, 2, default));
        Assert.Equal(1, decoder.Calls); Assert.Equal(3, commands.Text.Count);
        Assert.All(commands.Text, sql => {
            Assert.DoesNotContain("CsvBytes", sql); Assert.DoesNotContain("ManifestBytes", sql); Assert.DoesNotContain("FixtureBytes", sql);
        });
    }

    [Fact]
    public async Task ConcurrentColdLookups_MustFillOnce()
    {
        var store = new MemoryStore(); var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var decoder = new CountingDecoder { Started = started, Release = release };
        using var cache = new SnapshotCellCache();
        var calls = Enumerable.Range(0, 20).Select(_ => cache.LookupAsync(store, decoder, Cell, 2, default)).ToArray();
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10)); release.SetResult();
        var results = await Task.WhenAll(calls);
        Assert.Equal(1, decoder.Calls); Assert.Equal(1, store.FullReads);
        Assert.All(results, r => Assert.Equal("v1", r!.Publication.Version));
    }

    [Fact]
    public async Task CancelledColdFill_MustReleaseGateAndAllowWaitingReaderToRetry()
    {
        var store = new MemoryStore(); using var cancelled = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var decoder = new CountingDecoder { Started = started, Release = release }; using var cache = new SnapshotCellCache();
        var leader = cache.LookupAsync(store, decoder, Cell, 2, cancelled.Token);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        var follower = cache.LookupAsync(store, decoder, Cell, 2, default);
        cancelled.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => leader);
        release.SetResult();
        Assert.Equal("v1", (await follower.WaitAsync(TimeSpan.FromSeconds(10)))!.Publication.Version);
        Assert.Equal(2, decoder.Calls);
        await cache.LookupAsync(store, decoder, Cell, 2, default); Assert.Equal(2, decoder.Calls);
    }

    [Fact]
    public async Task ChangedVersionWithSameCsvHash_MustRefresh_FailedFillMustNotServeOldVersion()
    {
        var store = new MemoryStore(); var decoder = new CountingDecoder(); using var cache = new SnapshotCellCache();
        Assert.Equal(1, (await cache.LookupAsync(store, decoder, Cell, 2, default))!.Row!.Level);
        store.Current = store.Current! with { Version = "v2", ManifestBytes = [4] };
        var updated = (await cache.LookupAsync(store, decoder, Cell, 2, default))!;
        Assert.Equal("v2", updated.Publication.Version); Assert.Equal(2, updated.Row!.Level); Assert.Equal(2, decoder.Calls);
        store.Current = store.Current with { Version = "v3" }; decoder.FailNext = true;
        await Assert.ThrowsAsync<SnapshotValidationException>(() => cache.LookupAsync(store, decoder, Cell, 2, default));
        Assert.Equal("v3", (await cache.LookupAsync(store, decoder, Cell, 2, default))!.Publication.Version);
        Assert.Equal(4, decoder.Calls);
        store.Current = null; Assert.Null(await cache.LookupAsync(store, decoder, Cell, 2, default));
    }

    [Fact]
    public async Task PublicRateLimit_MustShareQuotaAcrossReads_AndSeparateForwardedClientIps()
    {
        using var baseFactory = new AuthFactory();
        using var factory = baseFactory.WithWebHostBuilder(b => b.ConfigureServices(s => s.AddSingleton<IStartupFilter, ConnectionStartupFilter>()));
        using var first = factory.CreateClient(new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://localhost") });
        first.DefaultRequestHeaders.Add("X-Test-Remote", "127.0.0.1"); first.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.11");
        for (var i = 0; i < 120; i++)
        {
            using var response = await first.GetAsync(i % 2 == 0 ? "/v1/risco/versao-atual" : "/v1/risco/grade");
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        using var rejected = await first.GetAsync("/v1/risco/celulas/89a8100c033ffff?faixa=2");
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
        Assert.Contains("LIMITE_TENTATIVAS", await rejected.Content.ReadAsStringAsync());
        first.DefaultRequestHeaders.Remove("X-Forwarded-For"); first.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.12");
        Assert.Equal(HttpStatusCode.NotFound, (await first.GetAsync("/v1/risco/versao-atual")).StatusCode);
    }

    private sealed class CountingDecoder : IProducerSnapshotDecoder
    {
        public int Calls; public bool FailNext; public TaskCompletionSource? Started; public TaskCompletionSource? Release;
        public async Task<DecodedSnapshot> DecodeAsync(SnapshotPackage package, CancellationToken ct)
        {
            Interlocked.Increment(ref Calls); Started?.TrySetResult();
            if (Release is not null) await Release.Task.WaitAsync(ct);
            if (FailNext) { FailNext = false; throw new SnapshotValidationException("SYNTHETIC_INVALID"); }
            return new(new[] { new RiskCell(Cell, 2, "com_dado", package.Version == "v1" ? 1 : 2, 1, 3) }, new HashSet<string> { Cell });
        }
    }
    private sealed class MemoryStore : ISnapshotPublicationStore
    {
        public PublishedSnapshot? Current = new("v1", new string('a', 64), DateTimeOffset.UnixEpoch, [1], [2], [3]);
        public int FullReads;
        public Task<PublishedSnapshot?> ReadCurrentAsync(CancellationToken ct) { Interlocked.Increment(ref FullReads); return Task.FromResult(Current); }
        public Task<SnapshotDescriptor?> ReadCurrentDescriptorAsync(CancellationToken ct) => Task.FromResult(Current is null ? null : new SnapshotDescriptor(Current.Version, Current.Sha256, Current.PublishedAtUtc));
        public Task<PublicationResult> PublishAsync(SnapshotPackage p, string sha, CancellationToken ct) => throw new NotSupportedException();
    }
    private sealed class ReadCommands : DbCommandInterceptor
    {
        public List<string> Text { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { Text.Add(command.CommandText); return ValueTask.FromResult(result); }
    }
}
