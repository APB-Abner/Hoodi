using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Hoodi.Application.Auth;
using Hoodi.Application.Push;
using Hoodi.Domain.Entities;
using Hoodi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Hoodi.Tests;

public sealed class LogoutIntegrationTests
{
    [Fact]
    public async Task Logout_MustRevokeCurrentFamilyAndPushWithoutAffectingOtherSession()
    {
        using var factory = new AuthFactory();
        var userId = await Seed(factory, "alice");
        using var current = factory.Client();
        using var other = factory.Client();
        var login = await Login(current, "alice");
        var otherLogin = await Login(other, "alice");
        await RegisterPush(current, "phone-a", "ExpoPushToken[SYNTHETIC_A]");
        await RegisterPush(other, "phone-b", "ExpoPushToken[SYNTHETIC_B]");
        var refresh = await current.PostAsJsonAsync("/v1/auth/renovar", new { refreshToken = login.RefreshToken });
        Assert.Equal(HttpStatusCode.OK, refresh.StatusCode);
        var rotated = (await refresh.Content.ReadFromJsonAsync<TokensAuth>())!;
        current.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", rotated.AccessToken);
        var result = await current.PostAsJsonAsync("/v1/auth/sair", new { instalacaoId = " phone-a " });
        Assert.Equal(HttpStatusCode.NoContent, result.StatusCode);
        Assert.Empty(await result.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.Unauthorized, (await current.GetAsync("/test/session")).StatusCode);
        foreach (var token in new[] { login.RefreshToken, rotated.RefreshToken })
            Assert.Equal(HttpStatusCode.Unauthorized, (await current.PostAsJsonAsync("/v1/auth/renovar", new { refreshToken = token })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync("/test/session")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await other.PostAsJsonAsync("/v1/auth/renovar", new { refreshToken = otherLogin.RefreshToken })).StatusCode);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoodiDbContext>();
        var pushes = await db.TokensPush.Where(x => x.UsuarioId == userId).ToListAsync();
        Assert.False(pushes.Single(x => x.InstalacaoId == "phone-a").Ativo);
        Assert.True(pushes.Single(x => x.InstalacaoId == "phone-b").Ativo);
        var revoked = await db.Sessoes.SingleAsync(x => x.UsuarioId == userId && x.RevogadoEmUtc != null);
        Assert.All(await db.RefreshTokens.Where(x => x.SessaoId == revoked.Id).ToListAsync(), x => Assert.Null(x.ReplayCifrado));
        Assert.Equal(HttpStatusCode.Unauthorized, (await current.PostAsJsonAsync("/v1/auth/sair", new { instalacaoId = "phone-a" })).StatusCode);
    }

    [Fact]
    public async Task Logout_WithoutPushOrWithForeignInstallation_MustNotTouchAnotherAccount()
    {
        using var factory = new AuthFactory();
        await Seed(factory, "alice");
        var bobId = await Seed(factory, "bob");
        using var alice = factory.Client();
        using var bob = factory.Client();
        await Login(alice, "alice");
        await Login(bob, "bob");
        await RegisterPush(bob, "shared-name", "ExpoPushToken[SYNTHETIC_BOB]");
        Assert.Equal(HttpStatusCode.NoContent, (await alice.PostAsJsonAsync("/v1/auth/sair", new { instalacaoId = "shared-name" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await bob.GetAsync("/test/session")).StatusCode);
        using var scope = factory.Services.CreateScope();
        Assert.True((await scope.ServiceProvider.GetRequiredService<HoodiDbContext>().TokensPush.SingleAsync(x => x.UsuarioId == bobId)).Ativo);
    }

    [Fact]
    public async Task Logout_MustRequireJwtAndRejectMissingOrSpoofedIdentityWithoutRevoking()
    {
        using var factory = new AuthFactory();
        using var client = factory.Client();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/v1/auth/sair", new { instalacaoId = "phone" })).StatusCode);
        await Seed(factory, "alice");
        await Login(client, "alice");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/auth/sair", new { })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/v1/auth/sair", new { instalacaoId = "phone", usuarioId = Guid.NewGuid() })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/test/session")).StatusCode);
    }

    private static async Task<Guid> Seed(AuthFactory factory, string name)
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<HoodiDbContext>();
        var user = new Usuario(name, name + "@example.invalid", "pending", factory.Clock.GetUtcNow());
        user.AlterarSenhaHash(scope.ServiceProvider.GetRequiredService<IPasswordHasher<Usuario>>().HashPassword(user, "synthetic-password-123"));
        db.Usuarios.Add(user);
        await db.SaveChangesAsync();
        return user.Id;
    }
    private static async Task<TokensAuth> Login(HttpClient client, string name)
    {
        var result = await client.PostAsJsonAsync("/v1/auth/login", new { email = name + "@example.invalid", senha = "synthetic-password-123" });
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
        var tokens = (await result.Content.ReadFromJsonAsync<TokensAuth>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.AccessToken);
        return tokens;
    }
    private static async Task RegisterPush(HttpClient client, string installation, string token)
    {
        var result = await client.PutAsJsonAsync("/v1/push/tokens", new TokenPushEntrada(token, "android", installation));
        Assert.Equal(HttpStatusCode.OK, result.StatusCode);
    }
}
