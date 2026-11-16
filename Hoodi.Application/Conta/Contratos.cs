using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hoodi.Application.Conta;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExclusaoContaEntrada([Required, StringLength(200)] string Senha);
public interface IContaService
{
    Task ExcluirAsync(Guid usuarioId, Guid sessaoId, ExclusaoContaEntrada entrada, CancellationToken ct);
}
