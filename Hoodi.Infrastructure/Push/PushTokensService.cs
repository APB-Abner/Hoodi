using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Hoodi.Application.Errors;
using Hoodi.Application.Push;
using Hoodi.Domain.Entities;
using Hoodi.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Hoodi.Infrastructure.Push;

public sealed class PushTokensService(HoodiDbContext db, IDataProtectionProvider protection, TimeProvider relogio) : IPushTokensService
{
    private readonly IDataProtector protector = protection.CreateProtector("hoodi.push-token.v1");
    public async Task<TokenPushResumo> RegistrarAsync(Guid usuarioId, TokenPushEntrada entrada, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(entrada.ExpoPushToken) ||
            !Regex.IsMatch(entrada.ExpoPushToken, @"\A(?:ExponentPushToken|ExpoPushToken)\[[^\r\n]+\]\z") ||
            entrada.Plataforma is not ("ios" or "android") || string.IsNullOrWhiteSpace(entrada.InstalacaoId) || entrada.InstalacaoId.Length > 100)
            throw new RequestException("TOKEN_PUSH_INVALIDO", 422);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(entrada.ExpoPushToken)));
        var instalacao = entrada.InstalacaoId.Trim();
        var agora = relogio.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var registro = await db.TokensPush.SingleOrDefaultAsync(x => x.UsuarioId == usuarioId && x.InstalacaoId == instalacao, ct);
        // A shared phone must not keep routing notifications to the previously logged-in account.
        var registroId = registro?.Id ?? Guid.Empty;
        var anteriores = await db.TokensPush.Where(x => x.ExpoTokenHash == hash && x.Ativo && x.Id != registroId).ToListAsync(ct);
        foreach (var anterior in anteriores) anterior.Desativar(agora);
        await db.SaveChangesAsync(ct);
        var cifrado = protector.Protect(entrada.ExpoPushToken);
        if (registro is null)
        {
            registro = new TokenPush(usuarioId, instalacao, entrada.Plataforma, hash, cifrado, agora);
            db.TokensPush.Add(registro);
        }
        else registro.Atualizar(entrada.Plataforma, hash, cifrado, agora);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new TokenPushResumo(registro.Id, registro.Ativo);
    }
    public async Task DesativarAsync(Guid usuarioId, Guid tokenId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var registro = await db.TokensPush.SingleOrDefaultAsync(x => x.Id == tokenId && x.UsuarioId == usuarioId, ct)
            ?? throw new RequestException("TOKEN_PUSH_NAO_ENCONTRADO", 404);
        registro.Desativar(relogio.GetUtcNow());
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
}

