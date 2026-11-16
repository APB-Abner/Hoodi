namespace Hoodi.Infrastructure.Panico;

public sealed class PedidoPanico
{
    public Guid Id { get; set; }
    public Guid? TitularId { get; set; }
    public Guid? IdempotencyKey { get; set; }
    public string ClasseCredencial { get; set; } = "JWT_usuario";
    public string Origem { get; set; } = "app";
    public DateTimeOffset RegistradoEm { get; set; }
    public DateTimeOffset? DadosExpiramEm { get; set; }
    public string? DadosCifrados { get; set; }
}
public sealed class DespachoPanico
{
    public Guid Id { get; set; }
    public Guid PedidoId { get; set; }
    public Guid? ContatoId { get; set; }
    public Guid? VinculoId { get; set; }
    public Guid? TokenPushId { get; set; }
    // Detect token replacement/transfer without storing another plaintext or encrypted copy.
    public string? TokenHash { get; set; }
    public string Estado { get; set; } = "pendente";
    public DateTimeOffset? AbertoEm { get; set; }
    public int Tentativas { get; set; }
    public DateTimeOffset ProximaTentativa { get; set; }
    public string? TicketId { get; set; }
}
public sealed class PanicoOptions
{
    // Operational default approved for this increment; team can revise the configured policy.
    public int? RetencaoLocalizacaoHoras { get; set; } = 24;
    public bool EnvioExpoHabilitado { get; set; }
}
internal sealed record DadosPanico(double? Latitude, double? Longitude, string? Observacao);
