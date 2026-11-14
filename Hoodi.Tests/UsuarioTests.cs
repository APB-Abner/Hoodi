
using Hoodi.Domain.Entities;
using Xunit;

namespace Hoodi.Tests;

public class UsuarioTests
{
    private static readonly DateTimeOffset Agora = new(2026, 11, 12, 12, 0, 0, TimeSpan.Zero);
    [Fact]
    public void CriarUsuario_ComDadosValidos_DeveNormalizarDados()
    {
        // Arrange
        string nome = "  Mari  ";
        string email = "  MARI@EMAIL.COM  ";
        string senhaHash = "hash_de_teste";

        // Act
        var usuario = new Usuario(nome, email, senhaHash, Agora);

        // Assert
        Assert.NotEqual(Guid.Empty, usuario.Id);
        Assert.Equal("Mari", usuario.Nome);
        Assert.Equal("mari@email.com", usuario.Email);
        Assert.Equal(senhaHash, usuario.SenhaHash);
        Assert.Equal(Agora, usuario.CriadoEmUtc);
        Assert.False(string.IsNullOrWhiteSpace(usuario.SecurityStamp));
    }

    [Fact]
    public void CriarUsuario_ComNomeVazio_DeveLancarExcecao()
    {
        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            new Usuario("", "mari@email.com", "hash_de_teste", Agora));
    }

    [Fact]
    public void AlterarSenhaHash_DeveAtualizarHashESecurityStamp()
    {
        // Arrange
        var usuario = new Usuario(
            "Mari",
            "mari@email.com",
            "hash_antigo", Agora);

        string stampAnterior = usuario.SecurityStamp;

        // Act
        usuario.AlterarSenhaHash("hash_novo");

        // Assert
        Assert.Equal("hash_novo", usuario.SenhaHash);
        Assert.NotEqual(stampAnterior, usuario.SecurityStamp);
    }

    [Fact]
    public void CriarUsuario_SemSenhaHash_DeveLancarExcecao()
    {
        Assert.Throws<ArgumentException>(() =>
            new Usuario("Mari", "mari@email.com", "", Agora));
    }

    [Fact]
    public void AlterarSenhaHash_ComHashVazio_DevePreservarDados()
    {
        // Arrange
        var usuario = new Usuario(
            "Mari",
            "mari@email.com",
            "hash_original", Agora);

        string stampOriginal = usuario.SecurityStamp;

        // Act & Assert
        Assert.Throws<ArgumentException>(() =>
            usuario.AlterarSenhaHash(""));

        Assert.Equal("hash_original", usuario.SenhaHash);
        Assert.Equal(stampOriginal, usuario.SecurityStamp);
    }

}


