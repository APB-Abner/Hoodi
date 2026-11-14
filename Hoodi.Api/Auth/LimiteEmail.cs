using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Memory;
using Hoodi.Application.Auth;

namespace Hoodi.Api.Auth;

// Single-instance development limiter. No raw email addresses are retained as keys.
public sealed class LimiteEmail(IMemoryCache cache)
{
    private readonly object gate = new();
    public void Verificar(string operacao, string endereco)
    {
        var key = operacao + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(endereco.Trim().ToLowerInvariant())));
        lock (gate)
        {
            if (!cache.TryGetValue<Contador>(key, out var contador))
            {
                contador = new Contador();
                cache.Set(key, contador, TimeSpan.FromMinutes(1));
            }
            if (++contador!.Quantidade > 5) throw new AutenticacaoException("LIMITE_TENTATIVAS", 429);
        }
    }
    private sealed class Contador { public int Quantidade; }
}
