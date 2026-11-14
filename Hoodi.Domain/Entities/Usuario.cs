
namespace Hoodi.Domain.Entities;

public class Usuario
{
    public Guid Id { get; private set; }
    public string Nome { get; private set; } = string.Empty;
    public string Email { get; private set; } = string.Empty;
    public string SenhaHash { get; private set; } = string.Empty;
    public string SecurityStamp { get; private set; } = string.Empty;
    public DateTime CriadoEmUtc { get; private set; }

    private Usuario() { }

    public Usuario(string nome, string email, string senhaHash, DateTimeOffset agoraUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nome);
        ArgumentException.ThrowIfNullOrWhiteSpace(email);
        ArgumentException.ThrowIfNullOrWhiteSpace(senhaHash);

        Id = Guid.NewGuid();
        Nome = nome.Trim();
        Email = email.Trim().ToLowerInvariant();
        SenhaHash = senhaHash;
        SecurityStamp = Guid.NewGuid().ToString("N");
        CriadoEmUtc = agoraUtc.UtcDateTime;
    }

    public void AlterarSenhaHash(string novoHash)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(novoHash);

        SenhaHash = novoHash;
        SecurityStamp = Guid.NewGuid().ToString("N");
    }
}

