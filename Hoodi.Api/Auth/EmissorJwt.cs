using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Hoodi.Domain.Entities;
using Hoodi.Infrastructure.Auth;
using Microsoft.IdentityModel.Tokens;

namespace Hoodi.Api.Auth;

public sealed class EmissorJwt(byte[] signingKey) : IEmissorAccessToken
{
    public const string Issuer = "hoodi.api";
    public const string Audience = "hoodi.app";
    public SymmetricSecurityKey Key { get; } = new(signingKey);

    public (string Token, DateTimeOffset ExpiraEm) Emitir(Usuario usuario, SessaoAutenticacao sessao, DateTimeOffset agora)
    {
        var expira = agora.AddMinutes(60);
        var jwt = new JwtSecurityToken(Issuer, Audience,
            [new Claim("sub", usuario.Id.ToString()), new Claim("sid", sessao.Id.ToString()),
             new Claim("security_stamp", usuario.SecurityStamp)],
            agora.UtcDateTime, expira.UtcDateTime,
            new SigningCredentials(Key, SecurityAlgorithms.HmacSha256));
        return (new JwtSecurityTokenHandler().WriteToken(jwt), expira);
    }
}

