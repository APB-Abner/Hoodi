using System.Text.Json.Serialization;
using Hoodi.Application.Rotas;

namespace Hoodi.Application.Lugares;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SugerirLugaresEntrada([property: JsonRequired] string Texto);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RecomendarLugaresEntrada([property: JsonRequired] PontoRota Posicao, [property: JsonRequired] string Categoria);

/// <summary>Lugar devolvido pelo provedor de busca (sem risco).</summary>
public sealed record LugarEncontrado(string Id, string Nome, string? Contexto, PontoRota Coordenada);
public sealed record LugarSugerido(string Id, string Rotulo, string? Contexto, PontoRota Coordenada);
/// <param name="Situacao">com_dado | sem_dado | fora_cobertura. sem_dado e fora_cobertura nunca são tranquilidade.</param>
public sealed record LugarRecomendado(string Id, string Nome, string? Contexto, PontoRota Coordenada, int DistanciaMetros, string Situacao, int? Nivel);
public sealed record SugestoesLugares(string Situacao, IReadOnlyList<LugarSugerido> Sugestoes);
public sealed record RecomendacaoLugares(string Situacao, string? Motivo, string? VersaoGrade, int? Faixa, string Aviso, IReadOnlyList<LugarRecomendado> Lugares);

public interface IProvedorLugares
{
    Task<IReadOnlyList<LugarEncontrado>> SugerirAsync(string texto, CancellationToken ct);
    Task<IReadOnlyList<LugarEncontrado>> BuscarPertoAsync(string termo, PontoRota centro, int raioMetros, CancellationToken ct);
}
public sealed class ProvedorLugaresException : Exception { }
public interface ILugares
{
    Task<SugestoesLugares> SugerirAsync(SugerirLugaresEntrada input, CancellationToken ct);
    Task<RecomendacaoLugares> RecomendarAsync(RecomendarLugaresEntrada input, CancellationToken ct);
}
