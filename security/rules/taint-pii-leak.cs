// Testes unitários das regras de taint (semgrep --test security/rules).
// "ruleid:" = a linha seguinte DEVE gerar finding; "ok:" = NÃO deve. Dados fictícios.
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

public sealed record LoginInput(string Email, string Senha);
public sealed record PanicInput(double Latitude, double Longitude, string DeviceId, string PetId);

public class TaintCases : ControllerBase
{
    private readonly ILogger<TaintCases> logger;
    private readonly HttpClient http;
    private readonly IPasswordHasher hasher;

    public void Password(LoginInput entrada)
    {
        // ruleid: hoodi-taint-password-leak
        logger.LogInformation("login {Senha}", entrada.Senha);
        var copia = entrada.Senha;
        // ruleid: hoodi-taint-password-leak
        Console.WriteLine($"senha={copia}");
        // ok: hoodi-taint-password-leak
        logger.LogInformation("hash {Hash}", hasher.HashPassword(entrada.Senha));
        // ok: hoodi-taint-password-leak
        logger.LogInformation("senha vazia? {Vazia}", string.IsNullOrWhiteSpace(entrada.Senha));
    }

    public IActionResult PasswordResponse(LoginInput entrada)
    {
        // ruleid: hoodi-taint-password-in-response
        return Ok(new { entrada.Senha });
    }

    public IActionResult PasswordResponseOk(LoginInput entrada)
    {
        // ok: hoodi-taint-password-in-response
        return Ok(new { entrada.Email });
    }

    public async Task Location(PanicInput entrada, IEnumerable<PanicInput> trajeto)
    {
        // ruleid: hoodi-taint-location-leak
        logger.LogWarning("panico em {Lat}", entrada.Latitude);
        // ruleid: hoodi-taint-location-leak
        await http.GetAsync($"https://maps.example.invalid/?q={entrada.Latitude},{entrada.Longitude}");
        var pontos = trajeto.Select(p => p.Longitude);
        // ruleid: hoodi-taint-location-leak
        File.AppendAllText("rota.txt", string.Join(";", pontos));
        // ok: hoodi-taint-location-leak
        logger.LogWarning("regiao {Area}", PiiMask.CoarseLocation(entrada.Latitude, entrada.Longitude));
        // ok: hoodi-taint-location-leak
        logger.LogWarning("dentro da area? {Dentro}", entrada.Latitude > -23.5);
    }

    public void Health(int heartRate)
    {
        var bpm = heartRate * 1;
        // ruleid: hoodi-taint-health-leak
        logger.LogError("bpm {Bpm}", bpm);
        // ok: hoodi-taint-health-leak
        logger.LogError("bpm alto {Alto}", heartRate >= 150);
    }

    public void Cpf(string cpf)
    {
        // ruleid: hoodi-taint-cpf-leak
        throw new ArgumentException("CPF invalido: " + cpf);
    }

    public void CpfMasked(string cpf)
    {
        // ok: hoodi-taint-cpf-leak
        throw new ArgumentException("CPF invalido: " + PiiMask.MaskCpf(cpf));
    }

    public async Task Token(string refreshToken, string expoToken)
    {
        // ruleid: hoodi-taint-token-leak
        logger.LogDebug("refresh {T}", refreshToken);
        // ok: hoodi-taint-token-leak
        await http.PostAsJsonAsync("https://exp.host/--/api/v2/push/send", new { to = expoToken });
        // ruleid: hoodi-taint-token-leak
        await http.PostAsJsonAsync("https://tracker.example.invalid/", new { to = expoToken });
    }

    public void Device(PanicInput entrada)
    {
        // ruleid: hoodi-taint-device-leak
        logger.LogInformation("device {D}", entrada.DeviceId);
        // ok: hoodi-taint-device-leak
        logger.LogInformation("device {D}", PiiMask.Hash(entrada.DeviceId));
        // ruleid: hoodi-taint-device-leak
        logger.LogWarning($"botão de ajuda do Pet {entrada.PetId}");
        // ok: hoodi-taint-device-leak
        logger.LogWarning("botão de ajuda do Pet {P}", PiiMask.Hash(entrada.PetId));
    }

    public void Contact(LoginInput entrada)
    {
        // ruleid: hoodi-taint-contact-leak
        logger.LogInformation("email {E}", entrada.Email);
    }
}
