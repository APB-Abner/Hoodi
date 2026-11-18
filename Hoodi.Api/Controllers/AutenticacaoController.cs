using Microsoft.AspNetCore.Authorization;
using System.Diagnostics;
using Hoodi.Application.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Hoodi.Api.Controllers;

[ApiController]
[Route("v1/auth")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
[EnableRateLimiting("auth")]
public sealed class AutenticacaoController(IAutenticacaoService auth, Hoodi.Api.Auth.LimiteEmail limite) : ControllerBase
{
    [HttpPost("registrar")]
    [ProducesResponseType<MensagemResultado>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> Registrar(CadastroEntrada entrada, CancellationToken ct)
    {
        limite.Verificar("registrar", entrada.Email);
        await RespostaGenericaAsync(() => auth.RegistrarAsync(entrada, ct), ct);
        return Accepted(new MensagemResultado("Se os dados forem elegiveis, a solicitacao de cadastro sera processada."));
    }

    [HttpPost("confirmar-email")]
    [ProducesResponseType<MensagemResultado>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Confirmar(ConfirmarEmailEntrada entrada, CancellationToken ct)
    {
        if (entrada.Nome.Trim().Length < 2) throw new AutenticacaoException("DADOS_INVALIDOS", 400);
        await auth.ConfirmarAsync(entrada, ct);
        return Ok(new MensagemResultado("E-mail confirmado. Faça login."));
    }

    [HttpPost("login")]
    [ProducesResponseType<TokensAuth>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TokensAuth>> Login(LoginEntrada entrada, CancellationToken ct)
    {
        limite.Verificar("login", entrada.Email);
        return Ok(await auth.LoginAsync(entrada, ct));
    }

    [HttpPost("renovar"), EnableRateLimiting("auth-renovar")]
    [ProducesResponseType<TokensAuth>(StatusCodes.Status200OK)]
    public async Task<ActionResult<TokensAuth>> Renovar(RenovarEntrada entrada, CancellationToken ct) =>
        Ok(await auth.RenovarAsync(entrada, ct));

    [HttpPost("solicitar-redefinicao")]
    [ProducesResponseType<MensagemResultado>(StatusCodes.Status202Accepted)]
    public async Task<IActionResult> SolicitarRedefinicao(CadastroEntrada entrada, CancellationToken ct)
    {
        limite.Verificar("reset", entrada.Email);
        await RespostaGenericaAsync(() => auth.SolicitarRedefinicaoAsync(entrada, ct), ct);
        return Accepted(new MensagemResultado("Se os dados forem elegiveis, as instrucoes serao enviadas."));
    }

    [HttpPost("redefinir-senha")]
    [ProducesResponseType<MensagemResultado>(StatusCodes.Status200OK)]
    public async Task<IActionResult> Redefinir(RedefinirSenhaEntrada entrada, CancellationToken ct)
    {
        await auth.RedefinirAsync(entrada, ct);
        return Ok(new MensagemResultado("Senha redefinida. Faça login novamente."));
    }

    [Authorize, HttpPost("sair")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Sair(SairEntrada entrada, CancellationToken ct)
    {
        await auth.SairAsync(Guid.Parse(User.FindFirst("sub")!.Value), Guid.Parse(User.FindFirst("sid")!.Value), entrada, ct);
        return NoContent();
    }
    private static async Task RespostaGenericaAsync(Func<Task> operacao, CancellationToken ct)
    {
        var inicio = Stopwatch.GetTimestamp();
        await operacao();
        var restante = TimeSpan.FromMilliseconds(300) - Stopwatch.GetElapsedTime(inicio);
        if (restante > TimeSpan.Zero) await Task.Delay(restante, ct);
    }
}



