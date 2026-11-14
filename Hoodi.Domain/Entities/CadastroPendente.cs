
using Hoodi.Domain.Exceptions;

namespace Hoodi.Domain.Entities;

public class CadastroPendente
{
    public Guid Id { get; private set; }
    public string Email { get; private set; } = string.Empty;
    public string TokenHash { get; private set; } = string.Empty;

    public DateTimeOffset CriadoEmUtc { get; private set; }
    public DateTimeOffset ExpiraEmUtc { get; private set; }
    public DateTimeOffset? ConsumidoEmUtc { get; private set; }

    private CadastroPendente() { }

    public CadastroPendente(
        string email,
        string tokenHash,
        DateTimeOffset agoraUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(tokenHash);

        Id = Guid.NewGuid();
        Email = email.Trim().ToLowerInvariant();
        TokenHash = tokenHash;

        CriadoEmUtc = agoraUtc.ToUniversalTime();
        ExpiraEmUtc = CriadoEmUtc.AddMinutes(30);
    }

    public bool EstaExpirado(DateTimeOffset agoraUtc)
    {
        return agoraUtc.ToUniversalTime() >= ExpiraEmUtc;
    }

    public void ValidarConfirmacao(
        string tokenHash,
        DateTimeOffset agoraUtc)
    {
        if (ConsumidoEmUtc is not null)
            throw new DomainException(
                "CADASTRO_JA_CONSUMIDO",
                "Este cadastro já foi confirmado.");

        if (EstaExpirado(agoraUtc))
            throw new DomainException(
                "TOKEN_EXPIRADO",
                "O token de confirmação expirou.");

        if (string.IsNullOrWhiteSpace(tokenHash) ||
            TokenHash != tokenHash)
            throw new DomainException(
                "TOKEN_INVALIDO",
                "O token informado é inválido.");
    }

    public void Consumir(
        string tokenHash,
        DateTimeOffset agoraUtc)
    {
        ValidarConfirmacao(tokenHash, agoraUtc);

        ConsumidoEmUtc = agoraUtc.ToUniversalTime();
    }

    public void ReemitirToken(
        string novoTokenHash,
        DateTimeOffset agoraUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(novoTokenHash);

        if (ConsumidoEmUtc is not null)
            throw new DomainException(
                "CADASTRO_JA_CONSUMIDO",
                "Não é possível reemitir um cadastro confirmado.");

        TokenHash = novoTokenHash;
        CriadoEmUtc = agoraUtc.ToUniversalTime();
        ExpiraEmUtc = CriadoEmUtc.AddMinutes(30);
    }
}
