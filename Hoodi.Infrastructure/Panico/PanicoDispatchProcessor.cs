using System.Security.Cryptography;
using System.Text.Json;
using Hoodi.Domain.Enums;
using Hoodi.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Hoodi.Infrastructure.Panico;

public sealed class PanicoDispatchProcessor(HoodiDbContext db, TimeProvider clock, IDataProtectionProvider protection, IExpoGateway expo)
{
    public async Task ProcessAsync(bool sendEnabled, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        await db.PedidosPanico.Where(x => x.DadosExpiramEm <= now && x.DadosCifrados != null)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.DadosCifrados, (string?)null), ct);
        if (!sendEnabled) return;
        // Single-instance SQLite deployment: serialize dispatch with revocation, logout and deletion.
        // Holding the transaction across the bounded HTTP call prevents sending an already-revoked association.
        var ids = await db.DespachosPanico.AsNoTracking().Where(x => (x.Estado == "pendente" || x.Estado == "aceito_expo") &&
            x.ProximaTentativa <= now).OrderBy(x => x.ProximaTentativa).Select(x => x.Id).Take(20).ToListAsync(ct);
        foreach (var id in ids)
        {
            db.ChangeTracker.Clear();
            now = clock.GetUtcNow();
            await using var tx = await db.Database.BeginTransactionAsync(ct);
            var d = await db.DespachosPanico.SingleAsync(x => x.Id == id, ct);
            if (d.Estado is not ("pendente" or "aceito_expo") || d.ProximaTentativa > now) continue;
            var p = await db.PedidosPanico.SingleAsync(x => x.Id == d.PedidoId, ct);
            var token = await db.TokensPush.SingleOrDefaultAsync(x => x.Id == d.TokenPushId && x.UsuarioId == d.ContatoId &&
                x.Ativo && x.ExpoTokenHash == d.TokenHash, ct);
            var authorized = p.TitularId != null && token != null && await db.Vinculos.AnyAsync(v => v.Id == d.VinculoId &&
                v.TitularId == p.TitularId && v.ContatoId == d.ContatoId && v.Status == StatusVinculo.Aceito, ct);
            if (!authorized) d.Estado = "cancelado";
            else if (now - p.RegistradoEm >= TimeSpan.FromHours(24)) d.Estado = "falho";
            else
            {
                d.Tentativas++;
                ExpoResult result;
                try
                {
                    result = d.Estado == "aceito_expo"
                        ? await expo.ReceiptAsync(d.TicketId!, ct)
                        : await expo.SendAsync(protection.CreateProtector("hoodi.push-token.v1").Unprotect(token!.TokenCifrado), p.Id, ct);
                }
                catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or JsonException)
                { result = new("retry"); }
                catch (CryptographicException) { result = new("retry"); }
                if (result.State == "retry")
                {
                    if (d.Estado == "pendente" && d.Tentativas >= 6) d.Estado = "falho";
                    d.ProximaTentativa = d.Estado == "pendente"
                        ? now.AddSeconds(Math.Min(60, Math.Pow(2, Math.Min(d.Tentativas, 6))))
                        : now.AddMinutes(15);
                }
                else
                {
                    d.Estado = result.State;
                    d.TicketId = result.Ticket ?? d.TicketId;
                    d.ProximaTentativa = now.AddMinutes(15);
                    if (result.DeviceNotRegistered) token!.Desativar(now);
                }
            }
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
        }
    }
}
