using Hoodi.Application.Panico;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Hoodi.Api.Controllers;

[ApiController, Authorize, Route("v1/panico")]
[ProducesResponseType<ProblemDetails>(401)]
public sealed class PanicoController(IPanicoService panico) : ControllerBase
{
    private Guid UsuarioId => Guid.Parse(User.FindFirst("sub")!.Value);
    [HttpPost]
    [ProducesResponseType<PanicoRegistrado>(202)]
    [ProducesResponseType<PanicoRegistrado>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(409)]
    [ProducesResponseType<ProblemDetails>(422)]
    [ProducesResponseType<ProblemDetails>(503)]
    public async Task<ActionResult<PanicoRegistrado>> Registrar(PanicoEntrada entrada, CancellationToken ct)
    {
        var result = await panico.RegistrarAsync(UsuarioId, entrada, ct);
        return StatusCode(result.Reutilizado ? 200 : 202, result);
    }
    [HttpGet("{pedidoId:guid}/status")]
    [ProducesResponseType<PanicoStatus>(200)]
    [ProducesResponseType<ProblemDetails>(404)]
    public async Task<ActionResult<PanicoStatus>> Status(Guid pedidoId, CancellationToken ct) => Ok(await panico.StatusAsync(UsuarioId, pedidoId, ct));
    [HttpGet("recebidos")]
    [ProducesResponseType<IReadOnlyList<PanicoRecebidoResumo>>(200)]
    public async Task<ActionResult<IReadOnlyList<PanicoRecebidoResumo>>> Recebidos(CancellationToken ct) => Ok(await panico.RecebidosAsync(UsuarioId, ct));
    [HttpGet("{pedidoId:guid}")]
    [ProducesResponseType<PanicoDetalhe>(200)]
    [ProducesResponseType<ProblemDetails>(404)]
    public async Task<ActionResult<PanicoDetalhe>> Detalhe(Guid pedidoId, CancellationToken ct) => Ok(await panico.DetalheAsync(UsuarioId, pedidoId, ct));
    [HttpPost("{pedidoId:guid}/abertura")]
    [ProducesResponseType(204)]
    [ProducesResponseType<ProblemDetails>(404)]
    public async Task<IActionResult> Abertura(Guid pedidoId, CancellationToken ct)
    {
        await panico.AbrirAsync(UsuarioId, pedidoId, ct);
        return NoContent();
    }
}
