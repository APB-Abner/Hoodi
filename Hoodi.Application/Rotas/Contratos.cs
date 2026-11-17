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
