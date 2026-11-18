// PROTÓTIPO DE AUDITORIA — versão CORRIGIDA do LeakyTelemetryController (não compilado).
// Mesmos fluxos, mas cada dado sensível passa por um sanitizador antes de chegar à saída.
// O Semgrep deve reportar ZERO findings aqui: é a prova de que os sanitizadores são reconhecidos.
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Hoodi.Security.Fixtures.Safe;

public sealed record TelemetryInput(string DeviceId, double Latitude, double Longitude, int HeartRate, string Cpf)
{
    // Records geram ToString() com todas as propriedades; aqui ele é sobrescrito.
    public override string ToString() => "TelemetryInput { dados sensíveis omitidos }";
}

public static class PiiMask
{
    // Reduz a precisão para ~1 km (célula grossa) — suficiente para estatística, não identifica a casa.
    public static string CoarseLocation(double lat, double lng) => $"{Math.Round(lat, 2)},{Math.Round(lng, 2)}";
    public static string MaskCpf(string cpf) => "***.***.***-" + cpf[^2..];
    public static string Hash(string value) =>
        Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(value)))[..12];
}

[ApiController, Route("v1/telemetria")]
public sealed class SafeTelemetryController(ILogger<SafeTelemetryController> logger, HttpClient http) : ControllerBase
{
    [HttpPost]
    public async Task<IActionResult> Receive([FromBody] TelemetryInput input)
    {
        logger.LogInformation("Telemetria recebida na região {Area}", PiiMask.CoarseLocation(input.Latitude, input.Longitude));

        var heartAlert = input.HeartRate > 150;
        logger.LogWarning("Alerta de batimento: {Alert}", heartAlert ? "sim" : "não");

        await http.PostAsJsonAsync("https://crm.example.invalid/leads", new { device = PiiMask.Hash(input.DeviceId) });
        Console.WriteLine("debug cpf=" + PiiMask.MaskCpf(input.Cpf));
        return Accepted();
    }
}
