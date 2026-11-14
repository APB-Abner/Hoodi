using Hoodi.Application.Contatos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hoodi.Api.Controllers;

[ApiController, Authorize, Route("v1/contatos")]
public sealed class ContatosController(IContatosService contatos) : ControllerBase
{
    private Guid UsuarioId => Guid.Parse(User.FindFirst("sub")!.Value);

    [HttpPost("convites"), EnableRateLimiting("convites-emitir")]
    [ProducesResponseType<NovoConvite>(StatusCodes.Status201Created)]
    public async Task<ActionResult<NovoConvite>> Emitir(CancellationToken ct) =>
        StatusCode(201, await contatos.EmitirAsync(UsuarioId, ct));

    [HttpPost("convites/resgatar"), EnableRateLimiting("convites-resgatar")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<VinculoResumo>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<VinculoResumo>> Resgatar(ResgateConviteEntrada entrada, CancellationToken ct)
    {
        var vinculo = await contatos.ResgatarAsync(UsuarioId, entrada, ct);
        return vinculo is null ? NoContent() : Ok(vinculo);
    }
    [HttpGet("convites")]
    public async Task<ActionResult<IReadOnlyList<ConvitePendenteResumo>>> ListarConvites(CancellationToken ct) =>
        Ok(await contatos.ListarConvitesAsync(UsuarioId, ct));

    [HttpDelete("convites/{conviteId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancelar(Guid conviteId, CancellationToken ct)
    {
        await contatos.CancelarConviteAsync(UsuarioId, conviteId, ct);
        return NoContent();
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<VinculoResumo>>> ListarVinculos(CancellationToken ct) =>
        Ok(await contatos.ListarVinculosAsync(UsuarioId, ct));

    [HttpDelete("{vinculoId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Revogar(Guid vinculoId, CancellationToken ct)
    {
        await contatos.RevogarAsync(UsuarioId, vinculoId, ct);
        return NoContent();
    }
}


