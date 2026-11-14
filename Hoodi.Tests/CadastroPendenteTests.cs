using Hoodi.Domain.Entities;
using Hoodi.Domain.Exceptions;

namespace Hoodi.Tests;

public class CadastroPendenteTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Reemitir_DeveInvalidarTokenAnteriorERenovarValidade()
    {
        var cadastro = new CadastroPendente("  USER@EXAMPLE.INVALID  ", "hash-anterior", Agora);
        cadastro.ReemitirToken("hash-novo", Agora.AddMinutes(10));

        Assert.Equal("user@example.invalid", cadastro.Email);
        Assert.Equal(Agora.AddMinutes(40), cadastro.ExpiraEmUtc);
        Assert.Throws<DomainException>(() => cadastro.Consumir("hash-anterior", Agora.AddMinutes(11)));
        Assert.Null(cadastro.ConsumidoEmUtc);
        cadastro.Consumir("hash-novo", Agora.AddMinutes(11));
        Assert.Equal(Agora.AddMinutes(11), cadastro.ConsumidoEmUtc);
    }

    [Fact]
    public void Consumir_NoInstanteDaExpiracao_DeveRejeitar()
    {
        var cadastro = new CadastroPendente("user@example.invalid", "hash", Agora);
        Assert.Throws<DomainException>(() => cadastro.Consumir("hash", Agora.AddMinutes(30)));
        Assert.Null(cadastro.ConsumidoEmUtc);
    }

    [Fact]
    public void CadastroConsumido_NaoPodeSerReutilizadoOuReemitido()
    {
        var cadastro = new CadastroPendente("user@example.invalid", "hash", Agora);
        cadastro.Consumir("hash", Agora.AddMinutes(1));
        Assert.Throws<DomainException>(() => cadastro.Consumir("hash", Agora.AddMinutes(2)));
        Assert.Throws<DomainException>(() => cadastro.ReemitirToken("novo-hash", Agora.AddMinutes(2)));
        Assert.Equal("hash", cadastro.TokenHash);
        Assert.Equal(Agora.AddMinutes(1), cadastro.ConsumidoEmUtc);
    }
}
