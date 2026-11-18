using Hoodi.Application.Errors;
using Hoodi.Application.Risk;
using Hoodi.Application.Rotas;

namespace Hoodi.Application.Lugares;

/// <summary>
/// Autocomplete de destino e lugares próximos ordenados pelo menor risco registrado.
/// Nada é persistido nem logado: texto, posição e resultados só existem durante o pedido.
/// </summary>
public sealed class LugaresService(IProvedorLugares provider, IGradeParaRotas grade, RouteScoring engine, TimeProvider clock) : ILugares
{
    public const int RaioMetros = 1500;
    public const int MaximoRecomendados = 8;
    public const int MaximoSugestoes = 5;
    /// <summary>Posição enviada ao provedor é arredondada (~110 m) para não revelar o ponto exato.</summary>
    private const int CasasDaPosicao = 3;
    /// <summary>Categoria do app → termo de busca. O app não escolhe o termo livre.</summary>
    public static readonly IReadOnlyDictionary<string, string> Categorias = new Dictionary<string, string>
    {
        ["farmacia"] = "farmácia", ["mercado"] = "mercado", ["hospital"] = "hospital",
        ["padaria"] = "padaria", ["cafe"] = "café", ["restaurante"] = "restaurante",
    };
    private const string Aviso = "Ordenado pelo risco histórico registrado da área do lugar na faixa de horário atual (menor primeiro), depois pela distância. Não indica que o lugar ou o caminho até ele sejam seguros. Sem dado não é tranquilidade.";

    public async Task<SugestoesLugares> SugerirAsync(SugerirLugaresEntrada input, CancellationToken ct)
    {
        var texto = (input.Texto ?? "").Trim();
        if (texto.Length is < 3 or > 80 || texto.Any(char.IsControl)) throw new RequestException("DADOS_INVALIDOS", 400);
        IReadOnlyList<LugarEncontrado> achados;
        try { achados = await provider.SugerirAsync(texto, ct); }
        catch (ProvedorLugaresException) { return new("indisponivel", []); }
        var lista = achados.Where(a => ValidoOuNulo(a.Coordenada)).DistinctBy(a => a.Id).Take(MaximoSugestoes)
            .Select(a => new LugarSugerido(a.Id, a.Nome, a.Contexto, a.Coordenada)).ToArray();
        return new("ok", lista);
    }

    public async Task<RecomendacaoLugares> RecomendarAsync(RecomendarLugaresEntrada input, CancellationToken ct)
    {
        RouteScoring.Validate(input.Posicao);
        if (!Categorias.TryGetValue(input.Categoria ?? "", out var termo)) throw new RequestException("DADOS_INVALIDOS", 400);
        RecomendacaoLugares Indisponivel(string motivo, string? versao = null, int? faixa = null) => new("indisponivel", motivo, versao, faixa, Aviso, []);
        var index = await grade.LerAtualAsync(ct);
        if (index is null) return Indisponivel("grade_indisponivel");
        var faixa = FaixasRisco.DaPartida(clock.GetUtcNow());
        if (!index.Rows.ContainsKey((engine.Cell(input.Posicao), faixa))) return Indisponivel("fora_territorio", index.Versao, faixa);
        var centro = new PontoRota(Math.Round(input.Posicao.Latitude, CasasDaPosicao), Math.Round(input.Posicao.Longitude, CasasDaPosicao));
        IReadOnlyList<LugarEncontrado> achados;
        try { achados = await provider.BuscarPertoAsync(termo, centro, RaioMetros, ct); }
        catch (ProvedorLugaresException) { return Indisponivel("provedor_indisponivel", index.Versao, faixa); }

        var avaliados = new List<LugarRecomendado>();
        foreach (var lugar in achados.Where(a => ValidoOuNulo(a.Coordenada)).DistinctBy(a => a.Id))
        {
            var distancia = (int)Math.Round(Metros(input.Posicao, lugar.Coordenada));
            if (distancia > RaioMetros) continue;
            var (situacao, nivel) = Classificar(index, engine.Cell(lugar.Coordenada), faixa);
            avaliados.Add(new(lugar.Id, lugar.Nome, lugar.Contexto, lugar.Coordenada, distancia, situacao, nivel));
        }
        var ordenados = avaliados
            .OrderBy(l => l.Situacao switch { "com_dado" => 0, "sem_dado" => 1, _ => 2 })
            .ThenBy(l => l.Nivel ?? int.MaxValue).ThenBy(l => l.DistanciaMetros).ThenBy(l => l.Id, StringComparer.Ordinal)
            .Take(MaximoRecomendados).ToArray();
        return new("ok", ordenados.Length == 0 ? "nenhum_lugar" : null, index.Versao, faixa, Aviso, ordenados);
    }

    private static (string Situacao, int? Nivel) Classificar(GradeParaRotas index, string cell, int faixa)
    {
        if (!index.Rows.TryGetValue((cell, faixa), out var linha)) return ("fora_cobertura", null);
        return linha.Status == "sem_dado" || linha.Level is null ? ("sem_dado", null) : ("com_dado", linha.Level);
    }

    private static bool ValidoOuNulo(PontoRota p)
    {
        try { RouteScoring.Validate(p); return true; } catch (RequestException) { return false; }
    }

    private static double Metros(PontoRota a, PontoRota b)
    {
        const double r = 6371000;
        double rad(double g) => g * Math.PI / 180;
        var dLat = rad(b.Latitude - a.Latitude); var dLon = rad(b.Longitude - a.Longitude);
        var h = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) + Math.Cos(rad(a.Latitude)) * Math.Cos(rad(b.Latitude)) * Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        return 2 * r * Math.Asin(Math.Min(1, Math.Sqrt(h)));
    }
}
