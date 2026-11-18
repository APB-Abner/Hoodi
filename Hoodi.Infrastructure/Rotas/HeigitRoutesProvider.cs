using System.Net.Http.Json;
using System.Text.Json;
using Hoodi.Application.Rotas;
using Microsoft.Extensions.Options;

namespace Hoodi.Infrastructure.Rotas;

public sealed class HeigitRoutesProvider(HttpClient client, IOptions<RotasOptions> options) : IProvedorRotas
{
    public async Task<IReadOnlyList<AlternativaRota>> ObterAsync(PontoRota origem, PontoRota destino, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ApiKey)) throw new ProvedorRotasException();
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.heigit.org/openrouteservice/v2/directions/foot-walking/geojson");
            request.Headers.TryAddWithoutValidation("Authorization", options.Value.ApiKey);
            request.Content = JsonContent.Create(new { coordinates = new[] { new[] { origem.Longitude, origem.Latitude }, new[] { destino.Longitude, destino.Latitude } },
                instructions = false, alternative_routes = new { target_count = 3, weight_factor = 1.4, share_factor = 0.6 } });
            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode) throw new ProvedorRotasException();
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            var features = json.RootElement.GetProperty("features").EnumerateArray().ToArray();
            if (features.Length > 3) throw new ProvedorRotasException();
            var result = new List<AlternativaRota>();
            foreach (var feature in features)
            {
                var geometry = feature.GetProperty("geometry");
                if (geometry.GetProperty("type").GetString() != "LineString") throw new ProvedorRotasException();
                var coords = geometry.GetProperty("coordinates").EnumerateArray().ToArray();
                if (coords.Length is < 2 or > 10000) throw new ProvedorRotasException();
                var points = coords.Select(p => new PontoRota(p[1].GetDouble(), p[0].GetDouble())).ToArray();
                foreach (var point in points) CoordenadasRota.Validar(point);
                var duration = feature.GetProperty("properties").GetProperty("summary").GetProperty("duration").GetDouble();
                if (!double.IsFinite(duration) || duration <= 0) throw new ProvedorRotasException();
                result.Add(new(points, duration));
            }
            return result;
        }
        catch (Exception ex) when (!ct.IsCancellationRequested && ex is HttpRequestException or TaskCanceledException or JsonException or KeyNotFoundException or InvalidOperationException or IndexOutOfRangeException or FormatException or Hoodi.Application.Errors.RequestException)
        { throw new ProvedorRotasException(); }
    }
}
