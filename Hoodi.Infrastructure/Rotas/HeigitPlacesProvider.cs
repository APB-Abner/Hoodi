using System.Globalization;
using System.Text.Json;
using Hoodi.Application.Lugares;
using Hoodi.Application.Rotas;
using Microsoft.Extensions.Options;

namespace Hoodi.Infrastructure.Rotas;

/// <summary>Busca de endereços e lugares (Pelias do HeiGIT/openrouteservice). Chave só no servidor; nada é guardado.</summary>
public sealed class HeigitPlacesProvider(HttpClient client, IOptions<RotasOptions> options) : IProvedorLugares
{
    private const string Base = "https://api.heigit.org/openrouteservice/geocode/";
    // Município de São Paulo (retângulo fixo no servidor; o app não escolhe a região).
    private const string RetanguloSp = "boundary.rect.min_lon=-46.83&boundary.rect.min_lat=-24.01&boundary.rect.max_lon=-46.36&boundary.rect.max_lat=-23.35&boundary.country=BR";

    public Task<IReadOnlyList<LugarEncontrado>> SugerirAsync(string texto, CancellationToken ct) =>
        ConsultarAsync("autocomplete?" + $"text={Uri.EscapeDataString(texto)}&size=8&layers=address,venue,street,neighbourhood&{RetanguloSp}", ct);

    public Task<IReadOnlyList<LugarEncontrado>> BuscarPertoAsync(string termo, PontoRota centro, int raioMetros, CancellationToken ct)
    {
        var lat = centro.Latitude.ToString("0.000", CultureInfo.InvariantCulture); var lon = centro.Longitude.ToString("0.000", CultureInfo.InvariantCulture);
        var km = (raioMetros / 1000.0).ToString("0.###", CultureInfo.InvariantCulture);
        return ConsultarAsync("search?" + $"text={Uri.EscapeDataString(termo)}&size=40&layers=venue&boundary.circle.lat={lat}&boundary.circle.lon={lon}&boundary.circle.radius={km}&boundary.country=BR", ct);
    }

    private async Task<IReadOnlyList<LugarEncontrado>> ConsultarAsync(string caminho, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ApiKey)) throw new ProvedorLugaresException();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, Base + caminho);
            request.Headers.TryAddWithoutValidation("Authorization", options.Value.ApiKey);
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) throw new ProvedorLugaresException();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var features = json.RootElement.GetProperty("features").EnumerateArray().ToArray();
            if (features.Length > 100) throw new ProvedorLugaresException();
            var result = new List<LugarEncontrado>();
            foreach (var feature in features)
            {
                var geometry = feature.GetProperty("geometry");
                if (geometry.GetProperty("type").GetString() != "Point") continue;
                var c = geometry.GetProperty("coordinates");
                var ponto = new PontoRota(c[1].GetDouble(), c[0].GetDouble());
                var props = feature.GetProperty("properties");
                var nome = Texto(props, "name") ?? Texto(props, "label");
                var id = Texto(props, "gid") ?? Texto(props, "id");
                if (nome is null || id is null) continue;
                result.Add(new(id, Limitar(Texto(props, "label") is { } rotulo && rotulo.StartsWith(nome, StringComparison.Ordinal) ? rotulo : nome, 120)!,
                    Limitar(Texto(props, "neighbourhood") ?? Texto(props, "locality"), 60), ponto));
            }
            return result;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or FormatException)
        { throw new ProvedorLugaresException(); }
    }

    private static string? Texto(JsonElement e, string nome) => e.TryGetProperty(nome, out var v) && v.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(v.GetString()) ? v.GetString() : null;
    private static string? Limitar(string? s, int max) => s is null ? null : (s.Length <= max ? s : s[..max]);
}
