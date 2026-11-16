using System.Net.Http.Json;
using System.Text.Json;

namespace Hoodi.Infrastructure.Panico;

public sealed record ExpoResult(string State, string? Ticket = null, bool DeviceNotRegistered = false);
public interface IExpoGateway
{
    Task<ExpoResult> SendAsync(string token, Guid pedidoId, CancellationToken ct);
    Task<ExpoResult> ReceiptAsync(string ticket, CancellationToken ct);
}
public sealed class ExpoGateway(HttpClient http) : IExpoGateway
{
    public async Task<ExpoResult> SendAsync(string token, Guid pedidoId, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("https://exp.host/--/api/v2/push/send", new
        {
            to = token, title = "Pedido de ajuda", body = "Abra o app para consultar um pedido de ajuda.",
            data = new { tipo = "pedido_ajuda", pedidoId }
        }, ct);
        if (!response.IsSuccessStatusCode) return Failure(response);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!json.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) return new("retry");
        if (data.TryGetProperty("status", out var status) && status.GetString() == "ok" &&
            data.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(id.GetString()))
            return new("aceito_expo", id.GetString());
        return Error(data);
    }
    public async Task<ExpoResult> ReceiptAsync(string ticket, CancellationToken ct)
    {
        using var response = await http.PostAsJsonAsync("https://exp.host/--/api/v2/push/getReceipts", new { ids = new[] { ticket } }, ct);
        if (!response.IsSuccessStatusCode) return Failure(response);
        using var json = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct);
        if (!json.RootElement.TryGetProperty("data", out var data) || !data.TryGetProperty(ticket, out var receipt)) return new("retry");
        if (receipt.TryGetProperty("status", out var status) && status.GetString() == "ok") return new("aceito_provedor");
        return Error(receipt);
    }
    private static ExpoResult Failure(HttpResponseMessage response) => new((int)response.StatusCode is 429 or >= 500 ? "retry" : "falho");
    private static ExpoResult Error(JsonElement data)
    {
        var error = data.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Object &&
            details.TryGetProperty("error", out var code) ? code.GetString() : null;
        return error is "MessageRateExceeded" ? new("retry") : new("falho", DeviceNotRegistered: error == "DeviceNotRegistered");
    }
}
