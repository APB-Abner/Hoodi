using Hoodi.Application.Push;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hoodi.Api.Controllers;

[ApiController, Authorize, Route("v1/push/tokens")]
public sealed class PushController(IPushTokensService tokens) : ControllerBase
{
    private Guid UsuarioId => Guid.Parse(User.FindFirst("sub")!.Value);

    [HttpPut]
    [ProducesResponseType<TokenPushResumo>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TokenPushResumo>> Registrar(TokenPushEntrada entrada, CancellationToken ct) =>
        Ok(await tokens.RegistrarAsync(UsuarioId, entrada, ct));

    [HttpDelete("{tokenId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Desativar(Guid tokenId, CancellationToken ct)
    {
        await tokens.DesativarAsync(UsuarioId, tokenId, ct);
        return NoContent();
    }
}
