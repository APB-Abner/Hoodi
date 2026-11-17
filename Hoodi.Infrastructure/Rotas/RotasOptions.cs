using Hoodi.Application.Rotas;

namespace Hoodi.Infrastructure.Rotas;

public sealed class RotasOptions
{
    public FonteRisco FonteRisco { get; set; } = FonteRisco.PontuacaoMensal;
    public TratamentoSemDado SemDado { get; set; } = TratamentoSemDado.Desconhecido;
    public string ApiKey { get; set; } = "";
}
