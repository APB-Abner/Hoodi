using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hoodi.Application.Panico;

[JsonConverter(typeof(PanicoEntradaConverter))]
public sealed record PanicoEntrada([Required] Guid? IdempotencyKey, [Required] string Origem,
    double? Latitude = null, double? Longitude = null, [StringLength(300)] string? Observacao = null)
{
    [JsonIgnore] public bool CoordenadasEstruturaisInvalidas { get; init; }
}
public sealed record ResumoDespachos(string Estado, int Total, int Pendentes, int AceitosExpo,
    int AceitosProvedor, int AbertosNoApp, int FalhosOuCancelados);
public sealed record PanicoRegistrado(Guid PedidoId, DateTimeOffset RegistradoEm, string Origem, ResumoDespachos Resumo, bool Reutilizado);
public sealed record PanicoStatus(Guid PedidoId, DateTimeOffset RegistradoEm, ResumoDespachos Resumo);
public sealed record PanicoRecebidoResumo(Guid PedidoId, string TitularNome, DateTimeOffset RegistradoEm);
public sealed record PanicoDetalhe(Guid PedidoId, string TitularNome, DateTimeOffset RegistradoEm, string Origem,
    double? Latitude, double? Longitude, string? Observacao, bool LocalizacaoDisponivel);
public interface IPanicoService
{
    Task<PanicoRegistrado> RegistrarAsync(Guid usuarioId, PanicoEntrada entrada, CancellationToken ct);
    Task<PanicoStatus> StatusAsync(Guid usuarioId, Guid pedidoId, CancellationToken ct);
    Task<IReadOnlyList<PanicoRecebidoResumo>> RecebidosAsync(Guid usuarioId, CancellationToken ct);
    Task<PanicoDetalhe> DetalheAsync(Guid usuarioId, Guid pedidoId, CancellationToken ct);
    Task AbrirAsync(Guid usuarioId, Guid pedidoId, CancellationToken ct);
}
