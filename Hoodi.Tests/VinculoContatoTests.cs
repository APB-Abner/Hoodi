
using Hoodi.Domain.Exceptions;
using Hoodi.Domain.Entities;
using Hoodi.Domain.Enums;
using Xunit;

namespace Hoodi.Tests;

public class VinculoContatoTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
    [Fact]
    public void CriarVinculo_ComDadosValidos_DeveEstarAtivo()
    {
        // Arrange
        var titularId = Guid.NewGuid();
        var contatoId = Guid.NewGuid();

        // Act
        var vinculo = new VinculoContato(titularId, contatoId, Agora);

        // Assert
        Assert.NotEqual(Guid.Empty, vinculo.Id);
        Assert.Equal(titularId, vinculo.TitularId);
        Assert.Equal(contatoId, vinculo.ContatoId);
        Assert.Equal(Agora, vinculo.CriadoEmUtc);
        Assert.Equal(StatusVinculo.Aceito, vinculo.Status);
        Assert.True(vinculo.EstaAtivo());
        Assert.Null(vinculo.RevogadoEmUtc);
    }

    [Fact]
    public void CriarVinculo_SemTitular_DeveLancarExcecao()
    {
        Assert.Throws<ArgumentException>(() =>
            new VinculoContato(
                Guid.Empty,
                Guid.NewGuid(), Agora));
    }

    [Fact]
    public void CriarVinculo_SemContato_DeveLancarExcecao()
    {
        Assert.Throws<ArgumentException>(() =>
            new VinculoContato(
                Guid.NewGuid(),
                Guid.Empty, Agora));
    }

    [Fact]
    public void CriarVinculo_ComMesmoUsuario_DeveLancarExcecao()
    {
        // Arrange
        var usuarioId = Guid.NewGuid();

        // Act & Assert
        var erro = Assert.Throws<DomainException>(() =>
            new VinculoContato(usuarioId, usuarioId, Agora));
        Assert.Equal("VINCULO_PROPRIO", erro.Code);
    }

    [Fact]
    public void RevogarVinculo_DeveDesativarContato()
    {
        // Arrange
        var vinculo = new VinculoContato(
            Guid.NewGuid(),
            Guid.NewGuid(), Agora);

        // Act
        vinculo.Revogar(vinculo.TitularId, Agora.AddMinutes(1));

        // Assert
        Assert.Equal(StatusVinculo.Revogado, vinculo.Status);
        Assert.False(vinculo.EstaAtivo());
        Assert.Equal(Agora.AddMinutes(1), vinculo.RevogadoEmUtc);
    }

    [Fact]
    public void RevogarVinculo_DuasVezes_DevePreservarData()
    {
        // Arrange
        var vinculo = new VinculoContato(
            Guid.NewGuid(),
            Guid.NewGuid(), Agora);

        vinculo.Revogar(vinculo.TitularId, Agora.AddMinutes(1));
        var primeiraRevogacao = vinculo.RevogadoEmUtc;

        // Act
        vinculo.Revogar(vinculo.TitularId, Agora.AddMinutes(1));

        // Assert
        Assert.Equal(StatusVinculo.Revogado, vinculo.Status);
        Assert.Equal(primeiraRevogacao, vinculo.RevogadoEmUtc);
    }

    [Fact]
    public void RevogarVinculo_PeloContato_DevePermitir()
    {
        var vinculo = new VinculoContato(
            Guid.NewGuid(),
            Guid.NewGuid(), Agora);

        vinculo.Revogar(vinculo.ContatoId, Agora.AddMinutes(1));

        Assert.Equal(StatusVinculo.Revogado, vinculo.Status);
        Assert.False(vinculo.EstaAtivo());
    }

    [Fact]
    public void RevogarVinculo_PorTerceiro_DeveRejeitar()
    {
        var vinculo = new VinculoContato(
            Guid.NewGuid(),
            Guid.NewGuid(), Agora);

        var terceiroId = Guid.NewGuid();

        var excecao = Assert.Throws<DomainException>(() =>
            vinculo.Revogar(terceiroId, Agora.AddMinutes(1)));

        Assert.Equal(
            "VINCULO_REVOGACAO_NAO_AUTORIZADA",
            excecao.Code);

        Assert.True(vinculo.EstaAtivo());
        Assert.Null(vinculo.RevogadoEmUtc);
    }

}



