using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace Hoodi.Application.Auth;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record CadastroEntrada([Required, EmailAddress] string Email);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ConfirmarEmailEntrada(
    [Required, StringLength(512, MinimumLength = 32)] string Token,
    [Required, StringLength(100, MinimumLength = 2)] string Nome,
    [Required, StringLength(128, MinimumLength = 10)] string Senha);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record LoginEntrada(
    [Required, EmailAddress] string Email,
    [Required, StringLength(128)] string Senha);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RenovarEntrada([Required, StringLength(512, MinimumLength = 16)] string RefreshToken);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record RedefinirSenhaEntrada(
    [Required, StringLength(512, MinimumLength = 32)] string Token,
    [Required, StringLength(128, MinimumLength = 10)] string NovaSenha);
public sealed record MensagemResultado(string Mensagem);
public sealed record UsuarioResumo(Guid Id, string Nome, string Email);
public sealed record TokensAuth(string AccessToken, string TokenType, DateTimeOffset ExpiraEm,
    UsuarioResumo Usuario, string RefreshToken, DateTimeOffset ExpiraRefreshEm);

public interface IAutenticacaoService
{
    Task RegistrarAsync(CadastroEntrada entrada, CancellationToken ct);
    Task ConfirmarAsync(ConfirmarEmailEntrada entrada, CancellationToken ct);
    Task<TokensAuth> LoginAsync(LoginEntrada entrada, CancellationToken ct);
    Task<TokensAuth> RenovarAsync(RenovarEntrada entrada, CancellationToken ct);
    Task SolicitarRedefinicaoAsync(CadastroEntrada entrada, CancellationToken ct);
    Task RedefinirAsync(RedefinirSenhaEntrada entrada, CancellationToken ct);
    Task<bool> ValidarSessaoAsync(Guid usuarioId, Guid sessaoId, string stamp, CancellationToken ct);
}

public sealed class AutenticacaoException(string code, int status) : Exception("Authentication request rejected.")
{
    public string Code { get; } = code;
    public int Status { get; } = status;
}

