
using Hoodi.Domain.Enums;

using Hoodi.Domain.Exceptions;


namespace Hoodi.Domain.Entities;

public class VinculoContato
{
    public Guid Id { get; private set; }
    public Guid TitularId { get; private set; }
    public Guid ContatoId { get; private set; }

    public StatusVinculo Status { get; private set; }

    public DateTimeOffset CriadoEmUtc { get; private set; }
    public DateTimeOffset? RevogadoEmUtc { get; private set; }

    private VinculoContato() { }

    public VinculoContato(Guid titularId, Guid contatoId, DateTimeOffset agoraUtc)
    {
        if (titularId == Guid.Empty)
            throw new ArgumentException(
                "O titular é obrigatório.",
                nameof(titularId));

        if (contatoId == Guid.Empty)
            throw new ArgumentException(
                "O contato é obrigatório.",
                nameof(contatoId));

        if (titularId == contatoId)
            throw new DomainException("VINCULO_PROPRIO", "Um usuário não pode ser contato de si mesmo.");

        Id = Guid.NewGuid();
        TitularId = titularId;
        ContatoId = contatoId;

        Status = StatusVinculo.Aceito;
        CriadoEmUtc = agoraUtc.ToUniversalTime();
    }

    
    public void Revogar(Guid solicitanteId, DateTimeOffset agoraUtc)
    {
        if (solicitanteId != TitularId &&
            solicitanteId != ContatoId)
        {
            throw new DomainException(
                "VINCULO_REVOGACAO_NAO_AUTORIZADA",
                "Somente o titular ou o contato podem revogar o vínculo.");
        }

        if (Status == StatusVinculo.Revogado)
            return;

        Status = StatusVinculo.Revogado;
        RevogadoEmUtc = agoraUtc.ToUniversalTime();
    }


    public bool EstaAtivo()
    {
        return Status == StatusVinculo.Aceito;
    }
}


