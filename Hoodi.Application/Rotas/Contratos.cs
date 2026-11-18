using Hoodi.Application.Risk;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hoodi.Application.Rotas;

public enum FonteRisco { PontuacaoMensal, Nivel }
public enum TratamentoSemDado { Desconhecido, RiscoBaixo }
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record PontoRota([property: JsonRequired] double Latitude, [property: JsonRequired] double Longitude);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record AvaliarRotasEntrada([Required] PontoRota Origem, [Required] PontoRota Destino, [Required] string SaidaEm);
public sealed record AlternativaRota(IReadOnlyList<PontoRota> Pontos, double DuracaoSegundos);
public sealed record TrechoRota(string H3Cell, double Metros);
public sealed record PontuacaoRota(double Acumulada, double? IndicePorKm, double MetrosTotal, double MetrosComDado,
    double PercentualSemDado, double PercentualForaCobertura, double CoberturaPercentual, bool DadoInsuficiente);
public interface IProvedorRotas
{
    Task<IReadOnlyList<AlternativaRota>> ObterAsync(PontoRota origem, PontoRota destino, CancellationToken ct);
}
public sealed class ProvedorRotasException : Exception { }
public interface IIndiceEspacialRotas { string Cell(PontoRota point); }
public sealed record GradeParaRotas(string Versao, IReadOnlyDictionary<(string Cell, int Band), RiskCell> Rows);
public interface IGradeParaRotas { Task<GradeParaRotas?> LerAtualAsync(CancellationToken ct); }
public sealed record PoliticaRotas(FonteRisco FonteRisco, TratamentoSemDado SemDado);
public interface IPoliticaRotas { PoliticaRotas Ler(); }
public interface IAvaliacaoRotas { Task<AvaliacaoRotas> AvaliarAsync(AvaliarRotasEntrada input, CancellationToken ct); }
public sealed record RotaAvaliada(string RotaId, double DuracaoSegundos, IReadOnlyList<PontoRota> Geometria, PontuacaoRota Pontuacao, int? Posicao, int Faixa);
public sealed record AvaliacaoRotas(string Situacao, string? Motivo, string? VersaoGrade, string FonteRisco, string SemDado, IReadOnlyList<RotaAvaliada> Rotas, string Aviso)
{
    public string UnidadeIndice => FonteRisco == "Nivel" ? "nivel_x_km" : "pontos_por_mes_hora_x_km";
}
