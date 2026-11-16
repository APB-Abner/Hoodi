using Hoodi.Application.Conta;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hoodi.Api.Controllers;

[ApiController, Authorize, Route("v1/conta"), EnableRateLimiting("auth")]
public sealed class ContaController(IContaService contas) : ControllerBase
{
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(403)]
    public async Task<IActionResult> Excluir([FromBody] ExclusaoContaEntrada entrada, CancellationToken ct)
    {
        await contas.ExcluirAsync(Guid.Parse(User.FindFirst("sub")!.Value), Guid.Parse(User.FindFirst("sid")!.Value), entrada, ct);
        return NoContent();
    }
}
