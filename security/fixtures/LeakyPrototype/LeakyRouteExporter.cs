// PROTÓTIPO DE AUDITORIA — NÃO FAZ PARTE DA API (não compilado). Dados fictícios.
// Simula um serviço que exporta o trajeto do cidadão, escrito com primary constructor
// (C# 12), o mesmo estilo usado na API real, para provar que as regras funcionam nele.
using Microsoft.Extensions.Logging;

namespace Hoodi.Security.Fixtures.Leaky;

public sealed record RoutePoint(double Latitude, double Longitude, string H3Cell, DateTimeOffset At);

public sealed class LeakyRouteExporter(ILogger<LeakyRouteExporter> logger)
{
    // Segredo fixo no código (exemplo fictício).
    private const string ApiKey = "sk_live_FAKE_hoodi_0000000000000000";

    public void Export(Guid userId, string refreshToken, IReadOnlyList<RoutePoint> route)
    {
        // VAZAMENTO 9 — trajeto completo (células H3) gravado em arquivo sem proteção.
        var cells = string.Join(";", route.Select(p => p.H3Cell));
        File.AppendAllText("/tmp/hoodi-routes.csv", $"{userId},{cells}\n");

        // VAZAMENTO 10 — token de sessão no log: quem lê o log consegue se passar pelo cidadão.
        logger.LogError("Falha ao exportar; token={Token}", refreshToken);
    }

    public static string FakeCpfForTests() => "123.456.789-09";
}
