// PROTÓTIPO DE AUDITORIA — NÃO FAZ PARTE DA API.
// Este arquivo NÃO é compilado (está fora do Hoodi.slnx). Ele simula um módulo de
// eventos do Hoodi Pet (o bichinho de bolso que vibra e muda de carinha) escrito sem
// cuidado com privacidade, com vazamentos PROPOSITAIS para demonstrar o Taint Analysis.
// O Pet não tem GPS: o celular repassa ao servidor o evento do botão do Pet junto
// com a posição dele.
// Todos os dados são fictícios.
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Hoodi.Security.Fixtures.Leaky;

public sealed record TelemetryInput(
    string PetId,
    double Latitude,
    double Longitude,
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
        _logger.LogInformation("Evento do Pet recebido em {Lat},{Lng}", input.Latitude, input.Longitude);

        // VAZAMENTO 2 — identificador do Pet no log: permite seguir a mesma pessoa ao longo do tempo.
        var pet = input.PetId;
        _logger.LogWarning($"Botão de ajuda segurado no Pet {pet}");

        // VAZAMENTO 3 — localização enviada a um serviço externo de analytics na URL.
        var url = $"https://analytics.example.invalid/track?lat={input.Latitude}&lng={input.Longitude}";
        await _http.GetAsync(url);

        // VAZAMENTO 4 — CPF e identificador do Pet enviados no corpo para terceiro.
        await _http.PostAsJsonAsync("https://crm.example.invalid/leads", new { cpf = input.Cpf, pet = input.PetId });

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
