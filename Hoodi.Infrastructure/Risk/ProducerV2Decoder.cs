using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Hoodi.Application.Risk;

namespace Hoodi.Infrastructure.Risk;

public sealed class ProducerV2Decoder(H3SpatialIndex spatial) : IProducerSnapshotDecoder
{
    public const string DeliveredSha256 = "2dddef701bdecb65142874980f1ae5aba7b2ac6ab2abad5c47b07f07ecc5080a";
    public const string Warning = "Melhor dado disponível no período. Não indica tempo real nem local seguro.";
    private static readonly string[] Names = ["madrugada", "manha", "tarde", "noite_cedo", "noite"];
    private static readonly string[] Intervals = ["[0,6)", "[6,12)", "[12,18)", "[18,21)", "[21,24)"];
    private const string Header = "h3_cell,faixa,faixa_nome,status,nivel,pontuacao_mensal,n_registros";

    public Task<DecodedSnapshot> DecodeAsync(SnapshotPackage package, CancellationToken ct)
    {
        try { return Task.FromResult(Decode(package, ct)); }
        catch (SnapshotValidationException) { throw; }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException or FormatException or OverflowException or InvalidOperationException or DecoderFallbackException or ArgumentOutOfRangeException)
        { throw new SnapshotValidationException("FORMATO_SNAPSHOT_INVALIDO"); }
    }
    private DecodedSnapshot Decode(SnapshotPackage package, CancellationToken ct)
    {
        using var manifest = JsonDocument.Parse(package.ManifestBytes);
        using var fixture = JsonDocument.Parse(package.FixtureBytes);
        RejectDuplicateProperties(manifest.RootElement); RejectDuplicateProperties(fixture.RootElement);
        var m = manifest.RootElement; var f = fixture.RootElement;
        Require(m.GetProperty("contrato_versao").GetInt32() == 2 && f.GetProperty("contrato_versao").GetInt32() == 2, "VERSAO_CONTRATO_INVALIDA");
        Require(m.GetProperty("h3_resolucao").GetInt32() == 9 && m.GetProperty("fuso").GetString() == "America/Sao_Paulo", "REFERENCIA_INVALIDA");
        Require(m.GetProperty("aviso").GetString() == Warning, "AVISO_INVALIDO");
        Require(m.GetProperty("fonte").GetProperty("orgao").GetString() == "SSP-SP" &&
            m.GetProperty("fonte").GetProperty("arquivo").GetString() == "SPDadosCriminais_2026.xlsx" &&
            System.Text.RegularExpressions.Regex.IsMatch(m.GetProperty("fonte").GetProperty("sha256").GetString() ?? "", "\\A[0-9a-f]{64}\\z"), "FONTE_INVALIDA");
        Require(m.GetProperty("formula_pontuacao").GetString() == "pontuacao_mensal = (soma dos pesos dos registros da chave em jan–set) ÷ 9 meses ÷ horas da faixa (6,6,6,3,3). Pesos: furto, tráfico, drogas para consumo, lesão culposa no trânsito 1; roubo, lesão corporal 3; homicídio, feminicídio, estupro 5; demais rubricas sem peso.", "FORMULA_INVALIDA");
        Require(m.GetProperty("natureza").GetString() == "Mapa histórico observado (jan–set 2026); não é previsão. O backtest do classificador é um experimento retrospectivo à parte.", "NATUREZA_INVALIDA");
        Require(DateTimeOffset.TryParse(m.GetProperty("gerado_em").GetString(), CultureInfo.InvariantCulture, DateTimeStyles.None, out _), "DATA_GERACAO_INVALIDA");
        Require(m.GetProperty("janela_dados").EnumerateArray().Select(x => x.GetString()).SequenceEqual(new[] { "2026-01-01", "2026-09-30" }), "JANELA_INVALIDA");
        Require(m.GetProperty("csv_sha256").GetString() == Convert.ToHexString(SHA256.HashData(package.CsvBytes)).ToLowerInvariant(), "CHECKSUM_INVALIDO");
        Require(m.GetProperty("privacidade").GetProperty("k_minimo").GetInt32() == 3, "PRIVACIDADE_INVALIDA");
        var bands = m.GetProperty("faixas").EnumerateArray().ToArray();
        Require(bands.Length == 5, "FAIXAS_INVALIDAS");
        for (var i = 0; i < 5; i++) Require(bands[i].GetProperty("faixa").GetInt32() == i && bands[i].GetProperty("faixa_nome").GetString() == Names[i] &&
            bands[i].GetProperty("intervalo_horas").GetString() == Intervals[i], "FAIXAS_INVALIDAS");
        var cuts = m.GetProperty("cortes_nivel").EnumerateArray().Select(x => x.GetDecimal()).ToArray();
        Require(cuts.Length == 3 && cuts[0] >= 0 && cuts[0] < cuts[1] && cuts[1] < cuts[2], "CORTES_INVALIDOS");
        var b = m.GetProperty("perimetro");
        var minLat = b.GetProperty("lat_min").GetDouble(); var maxLat = b.GetProperty("lat_max").GetDouble();
        var minLon = b.GetProperty("lon_min").GetDouble(); var maxLon = b.GetProperty("lon_max").GetDouble();
        Require(double.IsFinite(minLat + maxLat + minLon + maxLon) && minLat >= -90 && maxLat <= 90 && minLon >= -180 && maxLon <= 180 &&
            maxLat > minLat && maxLon > minLon && maxLat - minLat <= .1 && maxLon - minLon <= .1, "PERIMETRO_INVALIDO");
        var center = m.GetProperty("perimetro_centro");
        Require(center.GetProperty("lat").GetDouble() >= minLat && center.GetProperty("lat").GetDouble() <= maxLat &&
            center.GetProperty("lon").GetDouble() >= minLon && center.GetProperty("lon").GetDouble() <= maxLon &&
            m.GetProperty("perimetro_tamanho_km").EnumerateArray().Select(x => x.GetInt32()).SequenceEqual(new[] { 5, 5 }), "PERIMETRO_INVALIDO");
        var text = new UTF8Encoding(false, true).GetString(package.CsvBytes);
        Require(!text.Contains('\r') && !text.StartsWith('\uFEFF') && text.EndsWith('\n'), "CSV_CODIFICACAO_INVALIDA");
        var lines = text.Split('\n'); Require(lines[0] == Header && lines.Length <= 50002, "CSV_COLUNAS_INVALIDAS");
        var rows = new List<RiskCell>(); var keys = new Dictionary<(string, int), RiskCell>();
        string? previous = null;
        foreach (var line in lines.Skip(1).SkipLast(1))
        {
            ct.ThrowIfCancellationRequested();
            var parts = line.Split(','); Require(parts.Length == 7, "CSV_COLUNAS_INVALIDAS");
            var band = Int(parts[1]); Require(band is >= 0 and <= 4 && parts[2] == Names[band], "FAIXA_INVALIDA");
            var row = new RiskCell(parts[0], band, parts[3], parts[4] == "" ? null : Int(parts[4]),
                parts[5] == "" ? null : decimal.Parse(parts[5], NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture), Int(parts[6]));
            Require(spatial.IsValidResolution9(row.H3Cell) && row.H3Cell.All(c => !char.IsUpper(c)), "H3_INVALIDO");
            Require(keys.TryAdd((row.H3Cell, row.Band), row), "CHAVE_DUPLICADA");
            var sortKey = row.H3Cell + row.Band;
            Require(previous is null || string.CompareOrdinal(previous, sortKey) < 0, "CSV_ORDEM_INVALIDA"); previous = sortKey;
            if (row.Status == "sem_dado") Require(row.Level is null && row.MonthlyScore is null && row.RecordCount == 0, "SEM_DADO_INCONSISTENTE");
            else Require(row.Status == "com_dado" && row.RecordCount >= 3 && row.MonthlyScore > 0 &&
                row.Level == cuts.Count(c => row.MonthlyScore >= c), "COM_DADO_INCONSISTENTE");
            rows.Add(row);
        }
        Require(rows.Count == m.GetProperty("n_chaves").GetInt32() && rows.Count(x => x.Status == "com_dado") == m.GetProperty("n_com_dado").GetInt32() &&
            rows.Count(x => x.Status == "sem_dado") == m.GetProperty("n_sem_dado").GetInt32(), "CONTAGENS_INVALIDAS");
        var privacy = m.GetProperty("privacidade");
        var suppressedKeys = privacy.GetProperty("n_chaves_suprimidas").GetInt32(); var suppressedRows = privacy.GetProperty("n_registros_suprimidos").GetInt32();
        Require(suppressedKeys >= 0 && suppressedKeys <= rows.Count(x => x.Status == "sem_dado") && suppressedRows >= suppressedKeys &&
            // Producer total includes unweighted rubrics; CSV counts include only scorable records.
            suppressedRows <= (long)suppressedKeys * 2 && rows.Sum(x => (long)x.RecordCount) + suppressedRows <= m.GetProperty("total_registros_usados").GetInt64() &&
            m.GetProperty("total_linhas_base").GetInt64() >= m.GetProperty("total_registros_usados").GetInt64(), "CONTAGENS_INVALIDAS");
        var samples = f.GetProperty("chaves").EnumerateArray().ToArray(); var sampleKeys = new HashSet<(string, int)>();
        foreach (var item in samples)
        {
            var cell = item.GetProperty("h3_cell").GetString()!; var band = item.GetProperty("faixa").GetInt32();
            Require(sampleKeys.Add((cell, band)) && keys.ContainsKey((cell, band)), "FIXTURE_INVALIDA");
            var row = keys[(cell, band)];
            Require(item.GetProperty("faixa_nome").GetString() == Names[band] && item.GetProperty("status").GetString() == row.Status &&
                NullableInt(item.GetProperty("nivel")) == row.Level && NullableDecimal(item.GetProperty("pontuacao_mensal")) == row.MonthlyScore &&
                item.GetProperty("n_registros").GetInt32() == row.RecordCount, "FIXTURE_INVALIDA");
        }
        Require(samples.Length >= 5 && sampleKeys.Select(k => keys[k].Level).ToHashSet().SetEquals(new int?[] { null, 0, 1, 2, 3 }), "FIXTURE_INVALIDA");
        var coverage = spatial.Coverage(minLat, maxLat, minLon, maxLon);
        Require(coverage.SetEquals(rows.Select(x => x.H3Cell)), "COBERTURA_INVALIDA");
        return new(rows, coverage);
    }
    private static int Int(string text) => int.Parse(text, NumberStyles.None, CultureInfo.InvariantCulture);
    private static int? NullableInt(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetInt32();
    private static decimal? NullableDecimal(JsonElement e) => e.ValueKind == JsonValueKind.Null ? null : e.GetDecimal();
    private static void Require(bool valid, string code) { if (!valid) throw new SnapshotValidationException(code); }
    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>();
            foreach (var property in element.EnumerateObject()) { Require(names.Add(property.Name), "JSON_AMBIGUO"); RejectDuplicateProperties(property.Value); }
        }
        else if (element.ValueKind == JsonValueKind.Array) foreach (var item in element.EnumerateArray()) RejectDuplicateProperties(item);
    }
}
