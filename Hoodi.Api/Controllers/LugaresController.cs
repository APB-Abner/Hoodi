using Hoodi.Application.Lugares;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hoodi.Api.Controllers;

/// <summary>
/// Autocomplete de destino e lugares próximos por menor risco registrado. Usam POST para que o texto e a posição
/// não apareçam em URL (proxy, histórico, log). Nada é persistido.
/// </summary>
[ApiController, Authorize, Route("v1/lugares"), EnableRateLimiting("lugares")]
public sealed class LugaresController(ILugares lugares) : ControllerBase
{
    [HttpPost("sugerir"), RequestSizeLimit(1024)]
    [ProducesResponseType<SugestoesLugares>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(429)]
    public async Task<ActionResult<SugestoesLugares>> Sugerir(SugerirLugaresEntrada input, CancellationToken ct) =>
        Ok(await lugares.SugerirAsync(input, ct));

    [HttpPost("recomendar"), RequestSizeLimit(1024)]
    [ProducesResponseType<RecomendacaoLugares>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(422)]
    [ProducesResponseType<ProblemDetails>(429)]
    public async Task<ActionResult<RecomendacaoLugares>> Recomendar(RecomendarLugaresEntrada input, CancellationToken ct) =>
        Ok(await lugares.RecomendarAsync(input, ct));
}
