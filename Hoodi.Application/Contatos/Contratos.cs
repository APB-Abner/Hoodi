using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hoodi.Application.Contatos;

public sealed record NovoConvite(Guid ConviteId, [property: StringLength(64, MinimumLength = 64), RegularExpression("^[0-9A-F]{64}$")] string Codigo, DateTimeOffset ExpiraEm, string Direcao = "titular_para_contato");
public sealed record ConvitePendenteResumo(Guid ConviteId, DateTimeOffset ExpiraEm, string Estado = "pendente", string Direcao = "titular_para_contato");
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ResgateConviteEntrada
{
    [Required, StringLength(64, MinimumLength = 64), RegularExpression("^[0-9A-F]{64}$")]
    public string Codigo { get; init; }
    [Required, RegularExpression("^(aceitar|rejeitar)$")]
    public string Decisao { get; init; }

    [JsonConstructor]
    public ResgateConviteEntrada(string? codigo, string? decisao)
    {
        Codigo = (codigo ?? string.Empty).Trim().ToUpperInvariant();
        Decisao = decisao ?? string.Empty;
    }
}
public sealed record VinculoResumo(Guid Id, Guid TitularId, Guid ContatoId, string Estado,
    string Direcao, string NomeExibicao, string PapelDoSolicitante);

public interface IContatosService
{
    Task<NovoConvite> EmitirAsync(Guid titularId, CancellationToken ct);
    Task<IReadOnlyList<ConvitePendenteResumo>> ListarConvitesAsync(Guid titularId, CancellationToken ct);
    Task CancelarConviteAsync(Guid titularId, Guid conviteId, CancellationToken ct);
    Task<VinculoResumo?> ResgatarAsync(Guid contatoId, ResgateConviteEntrada entrada, CancellationToken ct);
    Task<IReadOnlyList<VinculoResumo>> ListarVinculosAsync(Guid usuarioId, CancellationToken ct);
    Task RevogarAsync(Guid usuarioId, Guid vinculoId, CancellationToken ct);
}




