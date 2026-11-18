using Hoodi.Application.Rotas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hoodi.Api.Controllers;

[ApiController, Authorize, Route("v1/rotas"), EnableRateLimiting("rotas")]
public sealed class RotasController(IAvaliacaoRotas avaliacao) : ControllerBase
{
    [HttpPost("avaliar"), RequestSizeLimit(4096)]
    [ProducesResponseType<AvaliacaoRotas>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(422)]
    [ProducesResponseType<ProblemDetails>(429)]
    public async Task<ActionResult<AvaliacaoRotas>> Avaliar(AvaliarRotasEntrada input, CancellationToken ct) =>
        Ok(await avaliacao.AvaliarAsync(input, ct));
}
