using System.Text.Json;
using System.Text.Json.Serialization;

namespace Hoodi.Application.Panico;

public sealed class PanicoEntradaConverter : JsonConverter<PanicoEntrada>
{
    public override PanicoEntrada Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        var root = document.RootElement;
        var wire = root.Deserialize<Wire>(options) ?? throw new JsonException();
        var hasLat = root.EnumerateObject().Any(p => p.Name.Equals("latitude", StringComparison.OrdinalIgnoreCase));
        var hasLon = root.EnumerateObject().Any(p => p.Name.Equals("longitude", StringComparison.OrdinalIgnoreCase));
        return new(wire.IdempotencyKey, wire.Origem!, wire.Latitude, wire.Longitude, wire.Observacao)
        { CoordenadasEstruturaisInvalidas = hasLat != hasLon };
    }
    public override void Write(Utf8JsonWriter writer, PanicoEntrada value, JsonSerializerOptions options) =>
        JsonSerializer.Serialize(writer, new Wire(value.IdempotencyKey, value.Origem, value.Latitude, value.Longitude, value.Observacao), options);

    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    private sealed record Wire(Guid? IdempotencyKey, string? Origem, double? Latitude, double? Longitude, string? Observacao);
}
