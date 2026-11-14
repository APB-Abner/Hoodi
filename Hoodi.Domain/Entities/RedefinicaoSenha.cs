using Hoodi.Domain.Exceptions;

namespace Hoodi.Domain.Entities;

public sealed class RedefinicaoSenha
{
    public Guid Id { get; private set; }
    public Guid UsuarioId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiraEmUtc { get; private set; }
    public DateTimeOffset? ConsumidoEmUtc { get; private set; }

    private RedefinicaoSenha() { }

    public RedefinicaoSenha(Guid usuarioId, string tokenHash, DateTimeOffset agoraUtc)
    {
        if (usuarioId == Guid.Empty)
            throw new ArgumentException("User is required.", nameof(usuarioId));
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);
        Id = Guid.NewGuid();
        UsuarioId = usuarioId;
        TokenHash = tokenHash;
        ExpiraEmUtc = agoraUtc.ToUniversalTime().AddMinutes(30);
    }

    public void Consumir(string tokenHash, DateTimeOffset agoraUtc)
    {
        if (ConsumidoEmUtc is not null || agoraUtc >= ExpiraEmUtc ||
            string.IsNullOrWhiteSpace(tokenHash) || TokenHash != tokenHash)
            throw new DomainException("TOKEN_INVALIDO", "Token inválido ou expirado.");

        ConsumidoEmUtc = agoraUtc.ToUniversalTime();
    }
}
