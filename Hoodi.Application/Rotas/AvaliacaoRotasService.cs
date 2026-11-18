using System.Globalization;
using System.Text.RegularExpressions;
using Hoodi.Application.Errors;
using Hoodi.Application.Risk;

namespace Hoodi.Application.Rotas;

public sealed class AvaliacaoRotasService(IGradeParaRotas grade, RouteScoring engine, IProvedorRotas provider,
    IPoliticaRotas config, TimeProvider clock) : IAvaliacaoRotas
{
    public async Task<AvaliacaoRotas> AvaliarAsync(AvaliarRotasEntrada input, CancellationToken ct)
    {
        RouteScoring.Validate(input.Origem); RouteScoring.Validate(input.Destino);
        if (!Regex.IsMatch(input.SaidaEm, "(?:Z|[+-][0-9]{2}:[0-9]{2})\\z") ||
            !DateTimeOffset.TryParse(input.SaidaEm, CultureInfo.InvariantCulture, DateTimeStyles.None, out var departure)) throw new RequestException("DADOS_INVALIDOS", 400);
        var now = clock.GetUtcNow();
        if (departure < now.AddMinutes(-5) || departure > now.AddHours(48)) throw new RequestException("SAIDA_FORA_JANELA", 422);
        var options = config.Ler();
        var index = await grade.LerAtualAsync(ct);
        AvaliacaoRotas Unavailable(string reason) => new("indisponivel", reason, null, options.FonteRisco.ToString(), options.SemDado.ToString(), [], Warning);
        if (index is null) return Unavailable("grade_indisponivel");
        var band = FaixasRisco.DaPartida(departure);
        if (!index.Rows.ContainsKey((engine.Cell(input.Origem), band)) || !index.Rows.ContainsKey((engine.Cell(input.Destino), band))) return Unavailable("fora_territorio");
        IReadOnlyList<AlternativaRota> routes;
        try { routes = await provider.ObterAsync(input.Origem, input.Destino, ct); }
        catch (ProvedorRotasException) { return Unavailable("provedor_indisponivel"); }
        if (routes.Count == 0) return Unavailable("sem_alternativas");
        if (routes.Count > 3) return Unavailable("provedor_indisponivel");
        if (routes.Any(r => !double.IsFinite(r.DuracaoSegundos) || r.DuracaoSegundos <= 0)) return Unavailable("provedor_indisponivel");
        if (routes.Any(r => r.DuracaoSegundos > (now.AddHours(48) - departure).TotalSeconds)) throw new RequestException("SAIDA_FORA_JANELA", 422);
        var scores = routes.Select(r => engine.Score(engine.Slice(r.Pontos, ct), band, index.Rows, options.FonteRisco, options.SemDado)).ToArray();
        var reason = scores.Any(s => s.DadoInsuficiente) ? "cobertura_insuficiente" : scores.Length == 1 ? "apenas_uma_rota" : null;
        var positions = reason is null ? engine.Rank(scores) : new int?[scores.Length];
        var output = routes.Select((r, i) => new RotaAvaliada("r" + (i + 1), Math.Round(r.DuracaoSegundos, 2, MidpointRounding.ToEven),
            r.Pontos, Display(scores[i]), positions[i], band)).OrderBy(r => r.Posicao ?? int.MaxValue).ThenBy(r => r.RotaId).ToArray();
        return new AvaliacaoRotas(reason is null ? "concluida" : "inconclusiva", reason, index.Versao,
            options.FonteRisco.ToString(), options.SemDado.ToString(), output, Warning);
    }
    private static PontuacaoRota Display(PontuacaoRota p) => p with { Acumulada = Round(p.Acumulada), IndicePorKm = p.IndicePorKm is double n ? Round(n) : null,
        MetrosTotal = Math.Round(p.MetrosTotal, 2, MidpointRounding.ToEven), MetrosComDado = Math.Round(p.MetrosComDado, 2, MidpointRounding.ToEven),
        PercentualSemDado = Round(p.PercentualSemDado), PercentualForaCobertura = Round(p.PercentualForaCobertura), CoberturaPercentual = Round(p.CoberturaPercentual) };
    private static double Round(double n) => Math.Round(n, 6, MidpointRounding.ToEven);
    private const string Warning = "Índice histórico observado (janela do snapshot publicado, ver manifest.janela_dados), demonstração não validada; não indica rota segura. SemDado=RiscoBaixo é uma hipótese de comparação, não evidência de risco baixo.";
}
