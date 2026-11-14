namespace Hoodi.Domain.Entities;

public sealed class TokenPush
{
    public Guid Id { get; private set; }
    public Guid UsuarioId { get; private set; }
    public string InstalacaoId { get; private set; } = string.Empty;
    public string Plataforma { get; private set; } = string.Empty;
    public string ExpoTokenHash { get; private set; } = string.Empty;
    public string TokenCifrado { get; private set; } = string.Empty;
    public bool Ativo { get; private set; }
    public DateTimeOffset AtualizadoEmUtc { get; private set; }

    private TokenPush() { }
    public TokenPush(Guid usuarioId, string instalacaoId, string plataforma, string hash, string cifrado, DateTimeOffset agoraUtc)
    {
        if (usuarioId == Guid.Empty) throw new ArgumentException("User is required.", nameof(usuarioId));
        ArgumentException.ThrowIfNullOrWhiteSpace(instalacaoId);
        Id = Guid.NewGuid();
        UsuarioId = usuarioId;
        InstalacaoId = instalacaoId;
        Atualizar(plataforma, hash, cifrado, agoraUtc);
    }
    public void Atualizar(string plataforma, string hash, string cifrado, DateTimeOffset agoraUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plataforma);
        ArgumentException.ThrowIfNullOrWhiteSpace(hash);
        ArgumentException.ThrowIfNullOrWhiteSpace(cifrado);
        Plataforma = plataforma;
        ExpoTokenHash = hash;
        TokenCifrado = cifrado;
        Ativo = true;
        AtualizadoEmUtc = agoraUtc.ToUniversalTime();
    }
    public void Desativar(DateTimeOffset agoraUtc)
    {
        if (!Ativo) return;
        Ativo = false;
        AtualizadoEmUtc = agoraUtc.ToUniversalTime();
    }
}
