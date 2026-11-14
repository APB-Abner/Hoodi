using System.Security.Cryptography;
using System.Text;
using Hoodi.Application.Contatos;
using Hoodi.Application.Errors;
using Hoodi.Domain.Entities;
using Hoodi.Domain.Enums;
using Hoodi.Domain.Exceptions;
using Hoodi.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hoodi.Infrastructure.Contatos;

public sealed class ContatosService(HoodiDbContext db, TimeProvider relogio, IOptions<ContatosOptions> options) : IContatosService
{
    public async Task<NovoConvite> EmitirAsync(Guid titularId, CancellationToken ct)
    {
        var codigo = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var convite = new ConviteContato(titularId, Hash(codigo), relogio.GetUtcNow(), TimeSpan.FromMinutes(options.Value.ValidadeConviteMinutos));
        db.Convites.Add(convite);
        await db.SaveChangesAsync(ct);
        return new NovoConvite(convite.Id, codigo, convite.ExpiraEmUtc);
    }
    public async Task<IReadOnlyList<ConvitePendenteResumo>> ListarConvitesAsync(Guid titularId, CancellationToken ct)
    {
        var agora = relogio.GetUtcNow();
        return await db.Convites.AsNoTracking()
            .Where(x => x.TitularId == titularId && x.Estado == EstadoConvite.Pendente && x.ExpiraEmUtc > agora)
            .OrderBy(x => x.CriadoEmUtc).ThenBy(x => x.Id)
            .Select(x => new ConvitePendenteResumo(x.Id, x.ExpiraEmUtc, "pendente", "titular_para_contato")).ToListAsync(ct);
    }
    public async Task CancelarConviteAsync(Guid titularId, Guid conviteId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var convite = await db.Convites.SingleOrDefaultAsync(x => x.Id == conviteId && x.TitularId == titularId, ct)
            ?? throw new RequestException("CONVITE_NAO_ENCONTRADO", 404);
        convite.Cancelar(relogio.GetUtcNow());
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
    public async Task<VinculoResumo?> ResgatarAsync(Guid contatoId, ResgateConviteEntrada entrada, CancellationToken ct)
    {
        if (entrada.Decisao is not ("aceitar" or "rejeitar")) throw new RequestException("DECISAO_INVALIDA", 400);
        var hash = Hash(entrada.Codigo);
        var agora = relogio.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var convite = await db.Convites.SingleOrDefaultAsync(x => x.CodigoHash == hash, ct)
            ?? throw new DomainException("CONVITE_INVALIDO", "Convite inválido ou encerrado.");
        convite.ValidarResgate(contatoId, agora);
        if (entrada.Decisao == "aceitar" && await db.Vinculos.AnyAsync(x => x.TitularId == convite.TitularId && x.ContatoId == contatoId && x.Status == StatusVinculo.Aceito, ct))
            throw new DomainException("VINCULO_DUPLICADO", "Já existe um vínculo ativo para este par.");
        convite.Resgatar(contatoId, entrada.Decisao == "aceitar", agora);
        VinculoContato? vinculo = null;
        if (entrada.Decisao == "aceitar")
        {
            vinculo = new VinculoContato(convite.TitularId, contatoId, agora);
            db.Vinculos.Add(vinculo);
        }
        await db.SaveChangesAsync(ct);
        var resultado = vinculo is null ? null : await ResumoAsync(vinculo, contatoId, ct);
        await tx.CommitAsync(ct);
        return resultado;
    }
    public async Task<IReadOnlyList<VinculoResumo>> ListarVinculosAsync(Guid usuarioId, CancellationToken ct)
    {
        return await (from vinculo in db.Vinculos.AsNoTracking()
            where vinculo.TitularId == usuarioId || vinculo.ContatoId == usuarioId
            join outro in db.Usuarios on (vinculo.TitularId == usuarioId ? vinculo.ContatoId : vinculo.TitularId) equals outro.Id
            orderby vinculo.CriadoEmUtc, vinculo.Id
            select new VinculoResumo(vinculo.Id, vinculo.TitularId, vinculo.ContatoId,
                vinculo.Status == StatusVinculo.Aceito ? "aceito" : "revogado", "titular_para_contato", outro.Nome,
                vinculo.TitularId == usuarioId ? "titular" : "contato")).ToListAsync(ct);
    }
    public async Task RevogarAsync(Guid usuarioId, Guid vinculoId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var vinculo = await db.Vinculos.SingleOrDefaultAsync(x => x.Id == vinculoId && (x.TitularId == usuarioId || x.ContatoId == usuarioId), ct)
            ?? throw new RequestException("VINCULO_NAO_ENCONTRADO", 404);
        vinculo.Revogar(usuarioId, relogio.GetUtcNow());
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
    private async Task<VinculoResumo> ResumoAsync(VinculoContato vinculo, Guid usuarioId, CancellationToken ct)
    {
        var outroId = vinculo.TitularId == usuarioId ? vinculo.ContatoId : vinculo.TitularId;
        var nome = await db.Usuarios.Where(x => x.Id == outroId).Select(x => x.Nome).SingleAsync(ct);
        return new VinculoResumo(vinculo.Id, vinculo.TitularId, vinculo.ContatoId, "aceito", "titular_para_contato", nome,
            vinculo.TitularId == usuarioId ? "titular" : "contato");
    }
    private static string Hash(string codigo) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(codigo.Trim().ToUpperInvariant())));
}


