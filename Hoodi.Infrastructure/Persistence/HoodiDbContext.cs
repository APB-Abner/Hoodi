using Hoodi.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Hoodi.Infrastructure.Persistence;

public sealed class HoodiDbContext(DbContextOptions<HoodiDbContext> options) : DbContext(options)
{
    public DbSet<Usuario> Usuarios => Set<Usuario>();
    public DbSet<CadastroPendente> Cadastros => Set<CadastroPendente>();
    public DbSet<RedefinicaoSenha> Redefinicoes => Set<RedefinicaoSenha>();
    public DbSet<SessaoAutenticacao> Sessoes => Set<SessaoAutenticacao>();
    public DbSet<RefreshRegistro> RefreshTokens => Set<RefreshRegistro>();
    public DbSet<EmailPendente> Emails => Set<EmailPendente>();
    public DbSet<ConviteContato> Convites => Set<ConviteContato>();
    public DbSet<VinculoContato> Vinculos => Set<VinculoContato>();
    public DbSet<TokenPush> TokensPush => Set<TokenPush>();

    protected override void OnModelCreating(ModelBuilder model)
    {
        var utcTicks = new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTimeOffset, long>(
            value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        model.Entity<ConviteContato>().Property(x => x.CriadoEmUtc).HasConversion(utcTicks);
        model.Entity<ConviteContato>().Property(x => x.ExpiraEmUtc).HasConversion(utcTicks);
        model.Entity<ConviteContato>().Property(x => x.EncerradoEmUtc).HasConversion(utcTicks);
        model.Entity<ConviteContato>().HasIndex(x => x.CodigoHash).IsUnique();
        model.Entity<ConviteContato>().HasIndex(x => new { x.TitularId, x.Estado, x.ExpiraEmUtc });
        model.Entity<ConviteContato>().HasOne<Usuario>().WithMany().HasForeignKey(x => x.TitularId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<ConviteContato>().HasOne<Usuario>().WithMany().HasForeignKey(x => x.ResgatadoPorId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<VinculoContato>().HasOne<Usuario>().WithMany().HasForeignKey(x => x.TitularId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<VinculoContato>().HasOne<Usuario>().WithMany().HasForeignKey(x => x.ContatoId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<VinculoContato>().HasIndex(x => new { x.TitularId, x.ContatoId }).IsUnique().HasFilter("\"Status\" = 1");
        model.Entity<TokenPush>().Property(x => x.AtualizadoEmUtc).HasConversion(utcTicks);
        model.Entity<TokenPush>().HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<TokenPush>().HasIndex(x => new { x.UsuarioId, x.InstalacaoId }).IsUnique();
        model.Entity<TokenPush>().HasIndex(x => x.ExpoTokenHash).IsUnique().HasFilter("\"Ativo\" = 1");
        model.Entity<Usuario>().HasIndex(x => x.Email).IsUnique();
        model.Entity<Usuario>().Property(x => x.Email).HasMaxLength(254);
        model.Entity<Usuario>().Property(x => x.Nome).HasMaxLength(100);
        model.Entity<CadastroPendente>().HasIndex(x => x.Email).IsUnique();
        model.Entity<CadastroPendente>().HasIndex(x => x.TokenHash).IsUnique();
        model.Entity<RedefinicaoSenha>().HasIndex(x => x.TokenHash).IsUnique();
        model.Entity<RedefinicaoSenha>().HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId);
        model.Entity<SessaoAutenticacao>().HasOne<Usuario>().WithMany().HasForeignKey(x => x.UsuarioId);
        model.Entity<RefreshRegistro>().HasKey(x => x.Hash);
        model.Entity<RefreshRegistro>().HasOne<SessaoAutenticacao>().WithMany().HasForeignKey(x => x.SessaoId);
    }
}

public sealed class RefreshRegistro
{
    public string Hash { get; set; } = string.Empty;
    public Guid SessaoId { get; set; }
    public DateTimeOffset? UsadoEmUtc { get; set; }
    public string? ReplayCifrado { get; set; }
}

// The outbox contains an encrypted payload, never a plaintext email/token pair.
public sealed class EmailPendente
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string ConteudoCifrado { get; set; } = string.Empty;
    public DateTimeOffset CriadoEmUtc { get; set; }
    public DateTimeOffset? EnviadoEmUtc { get; set; }
}

