using Hoodi.Domain.Entities;
using Hoodi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Hoodi.Tests;

public sealed class LegacyUtcPersistenceTests
{
    [Fact]
    public async Task ExistingTextTimestamps_MustReadAsUtcAndPreserveRevocation()
    {
        using var state = new DeploymentState();
        var options = new DbContextOptionsBuilder<HoodiDbContext>().UseSqlite("Data Source=" + state.DatabasePath).Options;
        await using var db = new HoodiDbContext(options);
        await db.Database.MigrateAsync();
        var owner = Guid.NewGuid();
        var contact = Guid.NewGuid();
        var activeId = Guid.NewGuid();
        var revokedId = Guid.NewGuid();
        const string created = "2026-11-12 23:30:00.1234567";
        const string revoked = "2026-11-13 00:15:00";
        const string syntheticHash = "SYNTHETIC_HASH";
        const string syntheticStamp = "SYNTHETIC_STAMP";
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Usuarios (Id, Nome, Email, SenhaHash, SecurityStamp, CriadoEmUtc) VALUES ({owner}, {"Owner"}, {"owner@example.invalid"}, {syntheticHash}, {syntheticStamp}, {created})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Usuarios (Id, Nome, Email, SenhaHash, SecurityStamp, CriadoEmUtc) VALUES ({contact}, {"Contact"}, {"contact@example.invalid"}, {syntheticHash}, {syntheticStamp}, {created})");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Vinculos (Id, TitularId, ContatoId, Status, CriadoEmUtc, RevogadoEmUtc) VALUES ({activeId}, {owner}, {contact}, {1}, {created}, NULL)");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO Vinculos (Id, TitularId, ContatoId, Status, CriadoEmUtc, RevogadoEmUtc) VALUES ({revokedId}, {owner}, {contact}, {2}, {created}, {revoked})");
        var expected = new DateTimeOffset(2026, 11, 12, 23, 30, 0, TimeSpan.Zero).AddTicks(1234567);
        var user = await db.Usuarios.SingleAsync(x => x.Id == owner);
        var active = await db.Vinculos.SingleAsync(x => x.Id == activeId);
        var history = await db.Vinculos.SingleAsync(x => x.Id == revokedId);
        Assert.Equal(expected, user.CriadoEmUtc);
        Assert.Equal(TimeSpan.Zero, user.CriadoEmUtc.Offset);
        Assert.Equal(expected, active.CriadoEmUtc);
        Assert.Null(active.RevogadoEmUtc);
        Assert.Equal(new DateTimeOffset(2026, 11, 13, 0, 15, 0, TimeSpan.Zero), history.RevogadoEmUtc);
        var now = new DateTimeOffset(2026, 11, 14, 10, 0, 0, TimeSpan.FromHours(-3));
        active.Revogar(owner, now);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var reloaded = await db.Vinculos.SingleAsync(x => x.Id == activeId);
        Assert.Equal(now.ToUniversalTime(), reloaded.RevogadoEmUtc);
        Assert.Equal(TimeSpan.Zero, reloaded.RevogadoEmUtc!.Value.Offset);
        Assert.Equal(expected, reloaded.CriadoEmUtc);
    }

    [Fact]
    public async Task NewOffsetTimestamps_MustNormalizeUtcAndRemainQueryable()
    {
        using var state = new DeploymentState();
        var options = new DbContextOptionsBuilder<HoodiDbContext>().UseSqlite("Data Source=" + state.DatabasePath).Options;
        await using var db = new HoodiDbContext(options);
        await db.Database.MigrateAsync();
        var local = new DateTimeOffset(2026, 11, 14, 23, 30, 0, TimeSpan.FromHours(-3));
        var owner = new Usuario("Owner", "owner@example.invalid", "SYNTHETIC_HASH", local);
        var contact = new Usuario("Contact", "contact@example.invalid", "SYNTHETIC_HASH", local.AddMinutes(5));
        var link = new VinculoContato(owner.Id, contact.Id, local);
        db.Usuarios.AddRange(owner, contact);
        db.Vinculos.Add(link);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        var loaded = await db.Usuarios.SingleAsync(x => x.Id == owner.Id);
        Assert.Equal(local.ToUniversalTime(), loaded.CriadoEmUtc);
        Assert.Equal(TimeSpan.Zero, loaded.CriadoEmUtc.Offset);
        var before = await db.Usuarios.Where(x => x.CriadoEmUtc < local.AddMinutes(1)).ToListAsync();
        Assert.Single(before);
        Assert.Equal(owner.Id, before[0].Id);
        var links = await db.Vinculos.OrderBy(x => x.CriadoEmUtc).ToListAsync();
        Assert.Single(links);
        Assert.Equal(local.ToUniversalTime(), links[0].CriadoEmUtc);
    }
}
