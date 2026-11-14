using Hoodi.Domain.Exceptions;

namespace Hoodi.Domain.Entities;

public enum EstadoConvite { Pendente = 0, Aceito = 1, Rejeitado = 2, Cancelado = 3 }

public sealed class ConviteContato
{
    public Guid Id { get; private set; }
    public Guid TitularId { get; private set; }
    public string CodigoHash { get; private set; } = string.Empty;
    public DateTimeOffset CriadoEmUtc { get; private set; }
    public DateTimeOffset ExpiraEmUtc { get; private set; }
    public EstadoConvite Estado { get; private set; }
    public Guid? ResgatadoPorId { get; private set; }
    public DateTimeOffset? EncerradoEmUtc { get; private set; }

    private ConviteContato() { }
    public ConviteContato(Guid titularId, string codigoHash, DateTimeOffset agoraUtc, TimeSpan validade)
    {
        if (titularId == Guid.Empty) throw new ArgumentException("Owner is required.", nameof(titularId));
        ArgumentException.ThrowIfNullOrWhiteSpace(codigoHash);
        if (validade <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(validade));
        Id = Guid.NewGuid();
        TitularId = titularId;
        CodigoHash = codigoHash;
        CriadoEmUtc = agoraUtc.ToUniversalTime();
        ExpiraEmUtc = CriadoEmUtc.Add(validade);
        Estado = EstadoConvite.Pendente;
    }
    public void ValidarResgate(Guid contatoId, DateTimeOffset agoraUtc)
    {
        ValidarPendente(agoraUtc);
        if (contatoId == Guid.Empty) throw new ArgumentException("Contact is required.", nameof(contatoId));
        if (contatoId == TitularId)
            throw new DomainException("VINCULO_PROPRIO", "Um usuário não pode ser contato de si mesmo.");
    }
    public void Resgatar(Guid contatoId, bool aceitar, DateTimeOffset agoraUtc)
    {
        ValidarResgate(contatoId, agoraUtc);
        Estado = aceitar ? EstadoConvite.Aceito : EstadoConvite.Rejeitado;
        ResgatadoPorId = contatoId;
        EncerradoEmUtc = agoraUtc.ToUniversalTime();
    }
    public void Cancelar(DateTimeOffset agoraUtc)
    {
        ValidarPendente(agoraUtc);
        Estado = EstadoConvite.Cancelado;
        EncerradoEmUtc = agoraUtc.ToUniversalTime();
    }
    private void ValidarPendente(DateTimeOffset agoraUtc)
    {
        if (Estado != EstadoConvite.Pendente || agoraUtc >= ExpiraEmUtc)
            throw new DomainException("CONVITE_ENCERRADO", "Convite inválido ou encerrado.");
    }
}

