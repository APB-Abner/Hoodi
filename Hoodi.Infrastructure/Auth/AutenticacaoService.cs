using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hoodi.Application.Auth;
using Hoodi.Domain.Entities;
using Hoodi.Domain.Exceptions;
using Hoodi.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Hoodi.Infrastructure.Auth;

public sealed class AutenticacaoService(
    HoodiDbContext db, IPasswordHasher<Usuario> senhas, IEmissorAccessToken emissor,
    IDataProtectionProvider protecao, TimeProvider relogio) : IAutenticacaoService
{
    private readonly IDataProtector replay = protecao.CreateProtector("hoodi.auth.refresh-replay.v1");
    private readonly IDataProtector email = protecao.CreateProtector("hoodi.auth.email-outbox.v1");
    private static readonly Usuario UsuarioFicticio = new("Dummy", "dummy@example.invalid", "placeholder", DateTimeOffset.UnixEpoch);
    private static readonly string HashFicticio = new PasswordHasher<Usuario>().HashPassword(UsuarioFicticio,
        Convert.ToHexString(RandomNumberGenerator.GetBytes(32)));

    public async Task RegistrarAsync(CadastroEntrada entrada, CancellationToken ct)
    {
        var endereco = Normalizar(entrada.Email);
        var agora = relogio.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await db.Usuarios.AnyAsync(x => x.Email == endereco, ct))
        {
            var pendente = await db.Cadastros.SingleOrDefaultAsync(x => x.Email == endereco, ct);
            if (pendente is null || agora - pendente.CriadoEmUtc >= TimeSpan.FromMinutes(1))
            {
                var token = NovoToken();
                if (pendente is null) db.Cadastros.Add(new CadastroPendente(endereco, Hash(token), agora));
                else pendente.ReemitirToken(Hash(token), agora);
                Enfileirar(endereco, "confirmar_email", token, agora);
            }
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task ConfirmarAsync(ConfirmarEmailEntrada entrada, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        var hash = Hash(entrada.Token);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var cadastro = await db.Cadastros.SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (cadastro is null || await db.Usuarios.AnyAsync(x => x.Email == cadastro.Email, ct))
            throw TokenInvalido();
        try { cadastro.Consumir(hash, agora); }
        catch (DomainException) { throw TokenInvalido(); }
        var usuario = new Usuario(entrada.Nome, cadastro.Email, "pending", agora);
        usuario.AlterarSenhaHash(senhas.HashPassword(usuario, entrada.Senha));
        db.Usuarios.Add(usuario);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task<TokensAuth> LoginAsync(LoginEntrada entrada, CancellationToken ct)
    {
        var endereco = Normalizar(entrada.Email);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var usuario = await db.Usuarios.SingleOrDefaultAsync(x => x.Email == endereco, ct);
        var resultado = senhas.VerifyHashedPassword(usuario ?? UsuarioFicticio,
            usuario?.SenhaHash ?? HashFicticio, entrada.Senha);
        if (usuario is null || resultado == PasswordVerificationResult.Failed)
            throw new AutenticacaoException("CREDENCIAIS_INVALIDAS", 401);
        if (resultado == PasswordVerificationResult.SuccessRehashNeeded)
            usuario.AlterarSenhaHash(senhas.HashPassword(usuario, entrada.Senha));
        var agora = relogio.GetUtcNow();
        var sessao = new SessaoAutenticacao(usuario.Id, usuario.SecurityStamp, agora);
        db.Sessoes.Add(sessao);
        var tokens = Emitir(usuario, sessao, agora);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return tokens;
    }

    public async Task<TokensAuth> RenovarAsync(RenovarEntrada entrada, CancellationToken ct)
    {
        var hash = Hash(entrada.RefreshToken);
        var agora = relogio.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var registro = await db.RefreshTokens.SingleOrDefaultAsync(x => x.Hash == hash, ct);
        if (registro is null) throw TokenInvalido();
        var sessao = await db.Sessoes.SingleAsync(x => x.Id == registro.SessaoId, ct);
        var usuario = await db.Usuarios.SingleAsync(x => x.Id == sessao.UsuarioId, ct);
        try { sessao.Validar(usuario.SecurityStamp, agora); }
        catch (DomainException) { throw TokenInvalido(); }
        if (registro.UsadoEmUtc is not null)
        {
            if (agora < registro.UsadoEmUtc.Value.AddSeconds(45) && registro.ReplayCifrado is not null)
                return JsonSerializer.Deserialize<TokensAuth>(replay.Unprotect(registro.ReplayCifrado))!;
            sessao.Revogar(agora);
            registro.ReplayCifrado = null;
            await db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);
            throw TokenInvalido();
        }
        sessao.Renovar(usuario.SecurityStamp, agora);
        var tokens = Emitir(usuario, sessao, agora);
        registro.UsadoEmUtc = agora;
        registro.ReplayCifrado = replay.Protect(JsonSerializer.Serialize(tokens));
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return tokens;
    }

    public async Task SolicitarRedefinicaoAsync(CadastroEntrada entrada, CancellationToken ct)
    {
        var endereco = Normalizar(entrada.Email);
        var agora = relogio.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var usuario = await db.Usuarios.SingleOrDefaultAsync(x => x.Email == endereco, ct);
        if (usuario is not null)
        {
            var anteriores = await db.Redefinicoes.Where(x => x.UsuarioId == usuario.Id && x.ConsumidoEmUtc == null).ToListAsync(ct);
            if (!anteriores.Any(x => x.ExpiraEmUtc > agora.AddMinutes(29)))
            {
                db.Redefinicoes.RemoveRange(anteriores);
                var token = NovoToken();
                db.Redefinicoes.Add(new RedefinicaoSenha(usuario.Id, Hash(token), agora));
                Enfileirar(endereco, "reset_senha", token, agora);
            }
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task RedefinirAsync(RedefinirSenhaEntrada entrada, CancellationToken ct)
    {
        var hash = Hash(entrada.Token);
        var agora = relogio.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var token = await db.Redefinicoes.SingleOrDefaultAsync(x => x.TokenHash == hash, ct);
        if (token is null) throw TokenInvalido();
        try { token.Consumir(hash, agora); }
        catch (DomainException) { throw TokenInvalido(); }
        var usuario = await db.Usuarios.SingleAsync(x => x.Id == token.UsuarioId, ct);
        usuario.AlterarSenhaHash(senhas.HashPassword(usuario, entrada.NovaSenha));
        foreach (var sessao in await db.Sessoes.Where(x => x.UsuarioId == usuario.Id && x.RevogadoEmUtc == null).ToListAsync(ct))
            sessao.Revogar(agora);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }

    public async Task SairAsync(Guid usuarioId, Guid sessaoId, SairEntrada entrada, CancellationToken ct)
    {
        var instalacao = entrada.InstalacaoId.Trim();
        var agora = relogio.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var sessao = await db.Sessoes.SingleOrDefaultAsync(x => x.Id == sessaoId && x.UsuarioId == usuarioId, ct);
        var usuario = await db.Usuarios.SingleOrDefaultAsync(x => x.Id == usuarioId, ct);
        if (sessao is null || usuario is null) throw TokenInvalido();
        try { sessao.Validar(usuario.SecurityStamp, agora); }
        catch (DomainException) { throw TokenInvalido(); }
        sessao.Revogar(agora);
        foreach (var push in await db.TokensPush.Where(x => x.UsuarioId == usuarioId && x.InstalacaoId == instalacao && x.Ativo).ToListAsync(ct))
            push.Desativar(agora);
        foreach (var refresh in await db.RefreshTokens.Where(x => x.SessaoId == sessaoId && x.ReplayCifrado != null).ToListAsync(ct))
            refresh.ReplayCifrado = null;
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
    public async Task<bool> ValidarSessaoAsync(Guid usuarioId, Guid sessaoId, string stamp, CancellationToken ct)
    {
        var usuario = await db.Usuarios.AsNoTracking().SingleOrDefaultAsync(x => x.Id == usuarioId, ct);
        var sessao = await db.Sessoes.AsNoTracking().SingleOrDefaultAsync(x => x.Id == sessaoId && x.UsuarioId == usuarioId, ct);
        if (usuario is null || sessao is null || usuario.SecurityStamp != stamp) return false;
        try { sessao.Validar(stamp, relogio.GetUtcNow()); return true; }
        catch (DomainException) { return false; }
    }

    private TokensAuth Emitir(Usuario usuario, SessaoAutenticacao sessao, DateTimeOffset agora)
    {
        var refresh = NovoToken();
        db.RefreshTokens.Add(new RefreshRegistro { Hash = Hash(refresh), SessaoId = sessao.Id });
        var access = emissor.Emitir(usuario, sessao, agora);
        return new TokensAuth(access.Token, "Bearer", access.ExpiraEm,
            new UsuarioResumo(usuario.Id, usuario.Nome, usuario.Email), refresh, sessao.ExpiraEmUtc);
    }

    private void Enfileirar(string endereco, string proposito, string token, DateTimeOffset agora) =>
        db.Emails.Add(new EmailPendente { CriadoEmUtc = agora,
            ConteudoCifrado = email.Protect(JsonSerializer.Serialize(new EmailAutenticacao(endereco, proposito, token))) });

    private static string Normalizar(string value) => value.Trim().ToLowerInvariant();
    private static string NovoToken() => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
    private static string Hash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    private static AutenticacaoException TokenInvalido() => new("TOKEN_INVALIDO", 401);
}



