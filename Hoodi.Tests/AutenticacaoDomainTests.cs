using Hoodi.Domain.Entities;
using Hoodi.Domain.Exceptions;

namespace Hoodi.Tests;

public class AutenticacaoDomainTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Redefinicao_DeveSerDeUsoUnico()
    {
        var token = new RedefinicaoSenha(Guid.NewGuid(), "hash", Agora);
        token.Consumir("hash", Agora.AddMinutes(1));
        Assert.Throws<DomainException>(() => token.Consumir("hash", Agora.AddMinutes(2)));
        Assert.Equal(Agora.AddMinutes(1), token.ConsumidoEmUtc);
    }

    [Theory]
    [InlineData("outro-hash", 1)]
    [InlineData("hash", 30)]
    [InlineData("hash", 31)]
    public void Redefinicao_Invalida_DevePreservarToken(string hash, int minutos)
    {
        var token = new RedefinicaoSenha(Guid.NewGuid(), "hash", Agora);
        Assert.Throws<DomainException>(() => token.Consumir(hash, Agora.AddMinutes(minutos)));
        Assert.Null(token.ConsumidoEmUtc);
    }

    [Fact]
    public void Sessao_DeveLimitarRenovacaoAoPrazoAbsoluto()
    {
        var sessao = new SessaoAutenticacao(Guid.NewGuid(), "stamp", Agora);
        foreach (var dias in new[] { 6, 12, 18, 24, 29 })
            sessao.Renovar("stamp", Agora.AddDays(dias));

        Assert.Equal(Agora.AddDays(30), sessao.ExpiraEmUtc);
        Assert.Throws<DomainException>(() => sessao.Renovar("stamp", Agora.AddDays(30)));
    }

    [Fact]
    public void Sessao_SemUsoPorSeteDias_DeveExpirar()
    {
        var sessao = new SessaoAutenticacao(Guid.NewGuid(), "stamp", Agora);
        Assert.Throws<DomainException>(() => sessao.Validar("stamp", Agora.AddDays(7)));
    }

    [Fact]
    public void TrocaDeSenha_DeveInvalidarSessaoAnterior()
    {
        var usuario = new Usuario("User", "user@example.invalid", "hash-anterior", Agora);
        var sessao = new SessaoAutenticacao(usuario.Id, usuario.SecurityStamp, Agora);
        usuario.AlterarSenhaHash("novo-hash");
        Assert.Throws<DomainException>(() => sessao.Validar(usuario.SecurityStamp, Agora.AddMinutes(1)));
    }

    [Fact]
    public void Revogacao_DeveImpedirRenovacaoEPreservarPrimeiraData()
    {
        var sessao = new SessaoAutenticacao(Guid.NewGuid(), "stamp", Agora);
        sessao.Revogar(Agora.AddMinutes(1));
        sessao.Revogar(Agora.AddMinutes(2));
        Assert.Equal(Agora.AddMinutes(1), sessao.RevogadoEmUtc);
        Assert.Throws<DomainException>(() => sessao.Renovar("stamp", Agora.AddMinutes(3)));
    }
}

