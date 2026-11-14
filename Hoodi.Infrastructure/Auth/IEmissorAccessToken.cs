using Hoodi.Domain.Entities;
using Hoodi.Application.Auth;

namespace Hoodi.Infrastructure.Auth;

public interface IEmissorAccessToken
{
    (string Token, DateTimeOffset ExpiraEm) Emitir(Usuario usuario, SessaoAutenticacao sessao, DateTimeOffset agora);
}

public sealed record EmailAutenticacao(string Email, string Proposito, string Token);
