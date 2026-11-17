using System.Globalization;
using System.Text.RegularExpressions;
using Hoodi.Application.Errors;
using Hoodi.Application.Risk;
using Hoodi.Application.Rotas;
using Hoodi.Infrastructure.Risk;
using Hoodi.Infrastructure.Rotas;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace Hoodi.Api.Controllers;

[ApiController, Authorize, Route("v1/rotas"), EnableRateLimiting("rotas")]
public sealed class RotasController(ISnapshotPublicationStore store, IProducerSnapshotDecoder decoder, SnapshotCellCache cache,
    RouteScoring engine, IProvedorRotas provider, IOptions<RotasOptions> config, TimeProvider clock) : ControllerBase
{
    [HttpPost("avaliar"), RequestSizeLimit(4096)]
    [ProducesResponseType<AvaliacaoRotas>(200)]
    [ProducesResponseType<ProblemDetails>(400)]
    [ProducesResponseType<ProblemDetails>(401)]
    [ProducesResponseType<ProblemDetails>(422)]
    [ProducesResponseType<ProblemDetails>(429)]
    public async Task<ActionResult<AvaliacaoRotas>> Avaliar(AvaliarRotasEntrada input, CancellationToken ct)
    {
        RouteScoring.Validate(input.Origem); RouteScoring.Validate(input.Destino);
        if (!Regex.IsMatch(input.SaidaEm, "(?:Z|[+-][0-9]{2}:[0-9]{2})\\z") ||
            !DateTimeOffset.TryParse(input.SaidaEm, CultureInfo.InvariantCulture, DateTimeStyles.None, out var departure)) throw new RequestException("DADOS_INVALIDOS", 400);
        var now = clock.GetUtcNow();
        if (departure < now.AddMinutes(-5) || departure > now.AddHours(48)) throw new RequestException("SAIDA_FORA_JANELA", 422);
        var options = config.Value;
        var index = await cache.ReadIndexAsync(store, decoder, ct);
        AvaliacaoRotas Unavailable(string reason) => new("indisponivel", reason, null, options.FonteRisco.ToString(), options.SemDado.ToString(), [], Warning);
        if (index is null) return Ok(Unavailable("grade_indisponivel"));
        var local = TimeZoneInfo.ConvertTime(departure, TimeZoneInfo.FindSystemTimeZoneById("America/Sao_Paulo"));
        int band = local.Hour < 6 ? 0 : local.Hour < 12 ? 1 : local.Hour < 18 ? 2 : local.Hour < 21 ? 3 : 4;
        if (!index.Rows.ContainsKey((RouteScoring.Cell(input.Origem), band)) || !index.Rows.ContainsKey((RouteScoring.Cell(input.Destino), band))) return Ok(Unavailable("fora_territorio"));
        IReadOnlyList<AlternativaRota> routes;
        try { routes = await provider.ObterAsync(input.Origem, input.Destino, ct); }
        catch (ProvedorRotasException) { return Ok(Unavailable("provedor_indisponivel")); }
        if (routes.Count == 0) return Ok(Unavailable("sem_alternativas"));
        if (routes.Count > 3) return Ok(Unavailable("provedor_indisponivel"));
        if (routes.Any(r => !double.IsFinite(r.DuracaoSegundos) || r.DuracaoSegundos <= 0)) return Ok(Unavailable("provedor_indisponivel"));
        if (routes.Any(r => r.DuracaoSegundos > (now.AddHours(48) - departure).TotalSeconds)) throw new RequestException("SAIDA_FORA_JANELA", 422);
        var scores = routes.Select(r => engine.Score(engine.Slice(r.Pontos, ct), band, index.Rows, options.FonteRisco, options.SemDado)).ToArray();
        var reason = scores.Any(s => s.DadoInsuficiente) ? "cobertura_insuficiente" : scores.Length == 1 ? "apenas_uma_rota" : null;
        var positions = reason is null ? engine.Rank(scores) : new int?[scores.Length];
        var output = routes.Select((r, i) => new RotaAvaliada("r" + (i + 1), Math.Round(r.DuracaoSegundos, 2, MidpointRounding.ToEven),
            r.Pontos, Display(scores[i]), positions[i], band)).OrderBy(r => r.Posicao ?? int.MaxValue).ThenBy(r => r.RotaId).ToArray();
        return Ok(new AvaliacaoRotas(reason is null ? "concluida" : "inconclusiva", reason, index.Descriptor.Version,
            options.FonteRisco.ToString(), options.SemDado.ToString(), output, Warning));
    }
    private static PontuacaoRota Display(PontuacaoRota p) => p with { Acumulada = Round(p.Acumulada), IndicePorKm = p.IndicePorKm is double n ? Round(n) : null,
        MetrosTotal = Math.Round(p.MetrosTotal, 2, MidpointRounding.ToEven), MetrosComDado = Math.Round(p.MetrosComDado, 2, MidpointRounding.ToEven),
        PercentualSemDado = Round(p.PercentualSemDado), PercentualForaCobertura = Round(p.PercentualForaCobertura), CoberturaPercentual = Round(p.CoberturaPercentual) };
    private static double Round(double n) => Math.Round(n, 6, MidpointRounding.ToEven);
    private const string Warning = "Índice histórico observado (jan–set/2026), demonstração não validada; não indica rota segura. SemDado=RiscoBaixo é uma hipótese de comparação, não evidência de risco baixo.";
}
public sealed record RotaAvaliada(string RotaId, double DuracaoSegundos, IReadOnlyList<PontoRota> Geometria, PontuacaoRota Pontuacao, int? Posicao, int Faixa);
public sealed record AvaliacaoRotas(string Situacao, string? Motivo, string? VersaoGrade, string FonteRisco, string SemDado, IReadOnlyList<RotaAvaliada> Rotas, string Aviso)
{
    public string UnidadeIndice => FonteRisco == "Nivel" ? "nivel_x_km" : "pontos_por_mes_hora_x_km";
}
