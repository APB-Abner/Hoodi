// PROTÓTIPO DE AUDITORIA — NÃO FAZ PARTE DA API.
// Este arquivo NÃO é compilado (está fora do Hoodi.slnx). Ele simula um módulo de
// telemetria do relógio hoodi escrito sem cuidado com privacidade, com vazamentos
// PROPOSITAIS para demonstrar o Taint Analysis em ação.
// Todos os dados são fictícios.
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Hoodi.Security.Fixtures.Leaky;

public sealed record TelemetryInput(
    string DeviceId,
    double Latitude,
    double Longitude,
    int HeartRate,
    string Cpf);

public sealed record CitizenSignup(string Nome, string Email, string Cpf, string Senha);

[ApiController, Route("v1/telemetria")]
public sealed class LeakyTelemetryController : ControllerBase
{
    private readonly ILogger<LeakyTelemetryController> _logger;
    private readonly HttpClient _http;

    public LeakyTelemetryController(ILogger<LeakyTelemetryController> logger, HttpClient http)
    {
        _logger = logger;
        _http = http;
    }

    [HttpPost]
    public async Task<IActionResult> Receive([FromBody] TelemetryInput input)
    {
        // VAZAMENTO 1 — localização exata do cidadão gravada no log da aplicação.
        _logger.LogInformation("Telemetria recebida em {Lat},{Lng}", input.Latitude, input.Longitude);

        // VAZAMENTO 2 — dado de saúde (LGPD art. 11, dado sensível) no log.
        var bpm = input.HeartRate;
        _logger.LogWarning($"Batimento alterado: {bpm} bpm");

        // VAZAMENTO 3 — localização enviada a um serviço externo de analytics na URL.
        var url = $"https://analytics.example.invalid/track?lat={input.Latitude}&lng={input.Longitude}";
        await _http.GetAsync(url);

        // VAZAMENTO 4 — CPF e dispositivo enviados no corpo para terceiro.
        await _http.PostAsJsonAsync("https://crm.example.invalid/leads", new { cpf = input.Cpf, device = input.DeviceId });

        // VAZAMENTO 5 — saída de depuração esquecida no console.
        Console.WriteLine("debug cpf=" + input.Cpf);

        return Accepted();
    }

    [HttpPost("cadastro")]
    public IActionResult Signup([FromBody] CitizenSignup signup)
    {
        // VAZAMENTO 6 — senha em texto puro no log.
        _logger.LogDebug("Novo cadastro {Email} com senha {Senha}", signup.Email, signup.Senha);

        // VAZAMENTO 7 — CPF dentro da mensagem de exceção (vai parar em logs e respostas de erro).
        if (signup.Nome.Length < 2)
            throw new InvalidOperationException("Cadastro inválido para o CPF " + signup.Cpf);

        // VAZAMENTO 8 — senha devolvida na resposta HTTP.
        return Ok(new { mensagem = "ok", senhaCadastrada = signup.Senha });
    }
}
