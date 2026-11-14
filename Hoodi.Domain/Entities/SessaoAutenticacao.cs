using Hoodi.Domain.Exceptions;

namespace Hoodi.Domain.Entities;

/// <summary>A refresh token family. Only token hashes belong in this entity.</summary>
public sealed class SessaoAutenticacao
{
    public Guid Id { get; private set; }
    public Guid UsuarioId { get; private set; }
    public string SecurityStamp { get; private set; } = string.Empty;
    public DateTimeOffset CriadoEmUtc { get; private set; }
    public DateTimeOffset ExpiraEmUtc { get; private set; }
    public DateTimeOffset ExpiraAbsolutoEmUtc { get; private set; }
    public DateTimeOffset? RevogadoEmUtc { get; private set; }

    private SessaoAutenticacao() { }

    public SessaoAutenticacao(Guid usuarioId, string securityStamp, DateTimeOffset agoraUtc)
    {
        if (usuarioId == Guid.Empty)
            throw new ArgumentException("User is required.", nameof(usuarioId));
        ArgumentException.ThrowIfNullOrWhiteSpace(securityStamp);
        Id = Guid.NewGuid();
        UsuarioId = usuarioId;
        SecurityStamp = securityStamp;
        CriadoEmUtc = agoraUtc.ToUniversalTime();
        ExpiraEmUtc = CriadoEmUtc.AddDays(7);
        ExpiraAbsolutoEmUtc = CriadoEmUtc.AddDays(30);
    }

    public void Validar(string securityStamp, DateTimeOffset agoraUtc)
    {
        if (RevogadoEmUtc is not null || agoraUtc >= ExpiraEmUtc ||
            agoraUtc >= ExpiraAbsolutoEmUtc || SecurityStamp != securityStamp)
            throw new DomainException("SESSAO_INVALIDA", "Autentique-se novamente.");
    }

    public void Renovar(string securityStamp, DateTimeOffset agoraUtc)
    {
        Validar(securityStamp, agoraUtc);
        var limiteDeslizante = agoraUtc.ToUniversalTime().AddDays(7);
        ExpiraEmUtc = limiteDeslizante < ExpiraAbsolutoEmUtc
            ? limiteDeslizante : ExpiraAbsolutoEmUtc;
    }

    public void Revogar(DateTimeOffset agoraUtc)
    {
        RevogadoEmUtc ??= agoraUtc.ToUniversalTime();
    }
}
