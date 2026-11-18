using Hoodi.Application.Errors;
using Hoodi.Application.Risk;

namespace Hoodi.Application.Rotas;

public sealed class RouteScoring(IIndiceEspacialRotas spatial)
{
    public static void Validate(PontoRota p) => CoordenadasRota.Validar(p);
    public string Cell(PontoRota p) { Validate(p); return spatial.Cell(p); }
    public static double Distance(PontoRota a, PontoRota b)
    {
        const double rad = Math.PI / 180;
        var x = Math.Pow(Math.Sin((b.Latitude - a.Latitude) * rad / 2), 2) + Math.Cos(a.Latitude * rad) * Math.Cos(b.Latitude * rad) * Math.Pow(Math.Sin((b.Longitude - a.Longitude) * rad / 2), 2);
        return 2 * 6371008.8 * Math.Asin(Math.Sqrt(Math.Clamp(x, 0, 1)));
    }
    public IReadOnlyList<TrechoRota> Slice(IReadOnlyList<PontoRota> points, CancellationToken ct = default)
    {
        if (points.Count is < 2 or > 10000) throw new RequestException("ROTA_INVALIDA", 422);
        var clean = new List<PontoRota>();
        foreach (var point in points) { Validate(point); if (clean.Count == 0 || point != clean[^1]) clean.Add(point); }
        if (clean.Count < 2) throw new RequestException("ROTA_INVALIDA", 422);
        var lengths = new Dictionary<string, double>(); var slices = 0;
        for (var segment = 1; segment < clean.Count; segment++)
        {
            var a = clean[segment - 1]; var b = clean[segment];
            var count = Math.Max(1, Math.Ceiling(Distance(a, b) / 5));
            if (count > 20000 - slices) throw new RequestException("ROTA_EXTENSA", 422);
            slices += (int)count; var previous = a;
            for (var i = 1; i <= count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var point = i == count ? b : new PontoRota(a.Latitude + i / count * (b.Latitude - a.Latitude), a.Longitude + i / count * (b.Longitude - a.Longitude));
                var cell = Cell(new((previous.Latitude + point.Latitude) / 2, (previous.Longitude + point.Longitude) / 2));
                lengths[cell] = lengths.GetValueOrDefault(cell) + Distance(previous, point); previous = point;
            }
        }
        return lengths.Select(x => new TrechoRota(x.Key, x.Value)).ToArray();
    }
    public PontuacaoRota Score(IReadOnlyList<TrechoRota> segments, int band, IReadOnlyDictionary<(string, int), RiskCell> snapshot, FonteRisco source, TratamentoSemDado missing)
    {
        if (band is < 0 or > 4 || !Enum.IsDefined(source) || !Enum.IsDefined(missing)) throw new RequestException("ROTA_INVALIDA", 422);
        double total = 0, known = 0, noData = 0, outside = 0, sum = 0;
        foreach (var s in segments)
        {
            if (!double.IsFinite(s.Metros) || s.Metros < 0) throw new RequestException("ROTA_INVALIDA", 422);
            total += s.Metros;
            if (!snapshot.TryGetValue((s.H3Cell, band), out var cell)) outside += s.Metros;
            else if (cell.Status == "sem_dado") noData += s.Metros;
            else { known += s.Metros; sum += (source == FonteRisco.PontuacaoMensal ? (double)cell.MonthlyScore!.Value : cell.Level!.Value) * s.Metros / 1000; }
        }
        if (total <= 0 || !double.IsFinite(total + sum)) throw new RequestException("ROTA_INVALIDA", 422);
        var effective = known + (missing == TratamentoSemDado.RiscoBaixo ? noData : 0);
        var unknownPct = 100 * (outside + (missing == TratamentoSemDado.Desconhecido ? noData : 0)) / total;
        return new(sum, effective > 0 ? sum / (effective / 1000) : null, total, known, 100 * noData / total, 100 * outside / total, 100 - unknownPct, unknownPct > 30);
    }
    public static bool Tie(double a, double b) => Math.Abs(a - b) <= 1e-9 * Math.Max(Math.Max(Math.Abs(a), Math.Abs(b)), 1);
    public int?[] Rank(IReadOnlyList<PontuacaoRota> scores)
    {
        var result = new int?[scores.Count]; var order = new List<int>();
        foreach (var scoreClass in Classes(Enumerable.Range(0, scores.Count).Where(i => !scores[i].DadoInsuficiente), i => scores[i].Acumulada))
            foreach (var distanceClass in Classes(scoreClass, i => scores[i].MetrosTotal / 1000)) order.AddRange(distanceClass.Order());
        for (var i = 0; i < order.Count; i++) result[order[i]] = i + 1;
        return result;
    }
    private static IEnumerable<List<int>> Classes(IEnumerable<int> values, Func<int, double> key)
    {
        List<int>? group = null;
        foreach (var i in values.OrderBy(key).ThenBy(i => i))
        {
            if (group is null || !Tie(key(group[0]), key(i))) { if (group is not null) yield return group; group = []; }
            group.Add(i);
        }
        if (group is not null) yield return group;
    }
}
