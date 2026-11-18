using System.Text.Json;
using Hoodi.Application.Conta;
using Hoodi.Application.Errors;
using Hoodi.Domain.Entities;
using Hoodi.Infrastructure.Auth;
using Hoodi.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hoodi.Infrastructure.Conta;

public sealed class ContaService(HoodiDbContext db, IPasswordHasher<Usuario> passwords, IDataProtectionProvider protection,
    TimeProvider clock) : IContaService
{
    public async Task ExcluirAsync(Guid usuarioId, Guid sessaoId, ExclusaoContaEntrada entrada, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var user = await db.Usuarios.SingleOrDefaultAsync(x => x.Id == usuarioId, ct) ?? throw new RequestException("AUTENTICACAO_INVALIDA", 401);
        var session = await db.Sessoes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sessaoId && x.UsuarioId == usuarioId, ct)
            ?? throw new RequestException("AUTENTICACAO_INVALIDA", 401);
        try { session.Validar(user.SecurityStamp, clock.GetUtcNow()); }
        catch (Hoodi.Domain.Exceptions.DomainException) { throw new RequestException("AUTENTICACAO_INVALIDA", 401); }
        if (passwords.VerifyHashedPassword(user, user.SenhaHash, entrada.Senha) == PasswordVerificationResult.Failed)
            throw new RequestException("SENHA_INVALIDA", 403);

        // Remove queued authentication mail too; never expose its decrypted payload.
        var protector = protection.CreateProtector(Hoodi.Application.Auth.ProtectionPurposes.AuthenticationEmailOutbox);
        foreach (var mail in await db.Emails.ToListAsync(ct))
        {
            try
            {
                var data = JsonSerializer.Deserialize<EmailAutenticacao>(protector.Unprotect(mail.ConteudoCifrado));
                if (data?.Email == user.Email) db.Emails.Remove(mail);
            }
            catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or JsonException)
            {
                // Preserve unrelated unreadable mail for recovery; never block another account's deletion.
            }
        }
        var ownRequests = db.PedidosPanico.Where(x => x.TitularId == usuarioId).Select(x => x.Id);
        // Preserve anonymous history only for recipients whose original consent is still active.
        await db.DespachosPanico.Where(d => ownRequests.Contains(d.PedidoId) &&
            !db.Vinculos.Any(v => v.Id == d.VinculoId && v.TitularId == usuarioId &&
                v.ContatoId == d.ContatoId && v.Status == Hoodi.Domain.Enums.StatusVinculo.Aceito))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ContatoId, (Guid?)null), ct);
        await db.DespachosPanico.Where(x => x.ContatoId == usuarioId)
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.ContatoId, (Guid?)null).SetProperty(x => x.VinculoId, (Guid?)null)
                .SetProperty(x => x.TokenPushId, (Guid?)null).SetProperty(x => x.TokenHash, (string?)null)
                .SetProperty(x => x.TicketId, (string?)null).SetProperty(x => x.AbertoEm, (DateTimeOffset?)null)
                .SetProperty(x => x.Estado, "cancelado"), ct);
        // Recipients retain their anonymous history. Remove only the deleted owner's identifiers/payload.
        await db.DespachosPanico.Where(x => ownRequests.Contains(x.PedidoId))
            .ExecuteUpdateAsync(s => s.SetProperty(x => x.VinculoId, (Guid?)null).SetProperty(x => x.TokenPushId, (Guid?)null)
                .SetProperty(x => x.TokenHash, (string?)null).SetProperty(x => x.TicketId, (string?)null).SetProperty(x => x.Estado, "cancelado"), ct);
        // Remove the deleted owner's ID, idempotency key, position and free text.
        await db.PedidosPanico.Where(x => x.TitularId == usuarioId).ExecuteUpdateAsync(s => s
            .SetProperty(x => x.TitularId, (Guid?)null).SetProperty(x => x.IdempotencyKey, (Guid?)null)
            .SetProperty(x => x.DadosCifrados, (string?)null).SetProperty(x => x.DadosExpiramEm, (DateTimeOffset?)null)
            .SetProperty(x => x.DadosTitularExpiramEm, (DateTimeOffset?)null), ct);
        await db.RefreshTokens.Where(x => db.Sessoes.Any(s => s.Id == x.SessaoId && s.UsuarioId == usuarioId)).ExecuteDeleteAsync(ct);
        await db.Sessoes.Where(x => x.UsuarioId == usuarioId).ExecuteDeleteAsync(ct);
        await db.Redefinicoes.Where(x => x.UsuarioId == usuarioId).ExecuteDeleteAsync(ct);
        await db.Cadastros.Where(x => x.Email == user.Email).ExecuteDeleteAsync(ct);
        await db.Convites.Where(x => x.TitularId == usuarioId || x.ResgatadoPorId == usuarioId).ExecuteDeleteAsync(ct);
        await db.Vinculos.Where(x => x.TitularId == usuarioId || x.ContatoId == usuarioId).ExecuteDeleteAsync(ct);
        await db.TokensPush.Where(x => x.UsuarioId == usuarioId).ExecuteDeleteAsync(ct);
        db.Usuarios.Remove(user);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}
