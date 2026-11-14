using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hoodi.Application.Push;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record TokenPushEntrada(
    [Required, RegularExpression(@"\A(?:ExponentPushToken|ExpoPushToken)\[[^\r\n]+\]\z")] string ExpoPushToken,
    [Required, RegularExpression("^(ios|android)$")] string Plataforma,
    [Required, StringLength(100, MinimumLength = 1)] string InstalacaoId);
public sealed record TokenPushResumo(Guid Id, bool Ativo);
public interface IPushTokensService
{
    Task<TokenPushResumo> RegistrarAsync(Guid usuarioId, TokenPushEntrada entrada, CancellationToken ct);
    Task DesativarAsync(Guid usuarioId, Guid tokenId, CancellationToken ct);
}
