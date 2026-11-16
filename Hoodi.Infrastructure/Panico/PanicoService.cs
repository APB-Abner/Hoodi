using System.Text.Json;
using Hoodi.Application.Errors;
using Hoodi.Application.Panico;
using Hoodi.Domain.Enums;
using Hoodi.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Hoodi.Infrastructure.Panico;

public sealed class PanicoService(HoodiDbContext db, TimeProvider clock, IDataProtectionProvider protection,
    IOptions<PanicoOptions> options) : IPanicoService
{
    private readonly IDataProtector protector = protection.CreateProtector("hoodi.panico.dados.v1");
    public async Task<PanicoRegistrado> RegistrarAsync(Guid usuarioId, PanicoEntrada entrada, CancellationToken ct)
    {
        if (entrada.IdempotencyKey is null || entrada.IdempotencyKey == Guid.Empty) throw new RequestException("DADOS_INVALIDOS", 400);
        if (entrada.Origem is not ("app" or "watch")) throw new RequestException("ORIGEM_CREDENCIAL_INCOMPATIVEL", 422);
        if (entrada.CoordenadasEstruturaisInvalidas || entrada.Latitude.HasValue != entrada.Longitude.HasValue ||
            entrada.Latitude is double lat && (!double.IsFinite(lat) || lat is < -90 or > 90) ||
            entrada.Longitude is double lon && (!double.IsFinite(lon) || lon is < -180 or > 180))
            throw new RequestException("COORDENADAS_INVALIDAS", 422);
        var now = clock.GetUtcNow();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        if (!await db.Usuarios.AnyAsync(x => x.Id == usuarioId, ct)) throw new RequestException("AUTENTICACAO_INVALIDA", 401);
        var existing = await db.PedidosPanico.SingleOrDefaultAsync(x => x.TitularId == usuarioId && x.IdempotencyKey == entrada.IdempotencyKey, ct);
        if (existing is not null)
        {
            if (existing.ClasseCredencial != "JWT_usuario") throw new RequestException("IDEMPOTENCIA_CREDENCIAL_DIVERGENTE", 409);
            return new(existing.Id, existing.RegistradoEm, existing.Origem, await Resumo(existing.Id, ct), true);
        }
        var sensitive = entrada.Latitude is not null || entrada.Observacao is not null;
        if (sensitive && options.Value.RetencaoLocalizacaoHoras is null)
            throw new RequestException("RETENCAO_NAO_CONFIGURADA", 503);
        var pedido = new PedidoPanico { Id = Guid.NewGuid(), TitularId = usuarioId, IdempotencyKey = entrada.IdempotencyKey,
            Origem = entrada.Origem, RegistradoEm = now,
            DadosExpiramEm = sensitive ? now.AddHours(options.Value.RetencaoLocalizacaoHoras!.Value) : null,
            DadosTitularExpiramEm = sensitive ? now.AddHours(options.Value.RetencaoTitularHoras) : null,
            DadosCifrados = sensitive ? protector.Protect(JsonSerializer.Serialize(new DadosPanico(entrada.Latitude, entrada.Longitude, entrada.Observacao))) : null };
        db.PedidosPanico.Add(pedido);
        var links = await db.Vinculos.Where(x => x.TitularId == usuarioId && x.Status == StatusVinculo.Aceito).ToListAsync(ct);
        foreach (var link in links)
        {
            var tokens = await db.TokensPush.Where(x => x.UsuarioId == link.ContatoId && x.Ativo).ToListAsync(ct);
            // Keep an explicit failed dispatch when an authorized contact has no active push.
            if (tokens.Count == 0) db.DespachosPanico.Add(new DespachoPanico { Id = Guid.NewGuid(), PedidoId = pedido.Id,
                ContatoId = link.ContatoId, VinculoId = link.Id, Estado = "falho", ProximaTentativa = now });
            foreach (var token in tokens) db.DespachosPanico.Add(new DespachoPanico { Id = Guid.NewGuid(), PedidoId = pedido.Id,
                ContatoId = link.ContatoId, VinculoId = link.Id, TokenPushId = token.Id, TokenHash = token.ExpoTokenHash, ProximaTentativa = now });
        }
        await db.SaveChangesAsync(ct);
        var result = new PanicoRegistrado(pedido.Id, now, pedido.Origem, await Resumo(pedido.Id, ct), false);
        await tx.CommitAsync(ct);
        return result;
    }
    public async Task<PanicoStatus> StatusAsync(Guid usuarioId, Guid pedidoId, CancellationToken ct)
    {
        var pedido = await db.PedidosPanico.AsNoTracking().SingleOrDefaultAsync(x => x.Id == pedidoId && x.TitularId == usuarioId, ct) ?? throw NotFound();
        return new(pedido.Id, pedido.RegistradoEm, await Resumo(pedido.Id, ct));
    }
    public async Task<IReadOnlyList<PanicoRecebidoResumo>> RecebidosAsync(Guid usuarioId, CancellationToken ct) =>
        await (from p in db.PedidosPanico.AsNoTracking()
            where db.DespachosPanico.Any(d => d.PedidoId == p.Id && d.ContatoId == usuarioId &&
                (p.TitularId == null || db.Vinculos.Any(v => v.Id == d.VinculoId && v.TitularId == p.TitularId && v.ContatoId == usuarioId && v.Status == StatusVinculo.Aceito)))
            orderby p.RegistradoEm descending, p.Id
            select new PanicoRecebidoResumo(p.Id, p.TitularId == null ? "Conta excluída" : db.Usuarios.Where(u => u.Id == p.TitularId).Select(u => u.Nome).First(), p.RegistradoEm)).ToListAsync(ct);
    public async Task<PanicoDetalhe> DetalheAsync(Guid usuarioId, Guid pedidoId, CancellationToken ct)
    {
        var p = await Authorized(usuarioId, pedidoId, false, ct);
        var nome = p.TitularId is null ? "Conta excluída" : await db.Usuarios.Where(x => x.Id == p.TitularId).Select(x => x.Nome).SingleAsync(ct);
        DadosPanico? data = null;
        var expires = p.TitularId == usuarioId ? p.DadosTitularExpiramEm ?? p.DadosExpiramEm : p.DadosExpiramEm;
        if (p.TitularId is not null && p.DadosCifrados is not null && expires > clock.GetUtcNow())
            data = JsonSerializer.Deserialize<DadosPanico>(protector.Unprotect(p.DadosCifrados));
        return new(p.Id, nome, p.RegistradoEm, p.Origem, data?.Latitude, data?.Longitude, data?.Observacao, data?.Latitude is not null);
    }
    public async Task AbrirAsync(Guid usuarioId, Guid pedidoId, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await Authorized(usuarioId, pedidoId, true, ct);
        foreach (var d in await db.DespachosPanico.Where(x => x.PedidoId == pedidoId && x.ContatoId == usuarioId).ToListAsync(ct))
            d.AbertoEm ??= clock.GetUtcNow();
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
    }
    private async Task<PedidoPanico> Authorized(Guid user, Guid id, bool contactOnly, CancellationToken ct)
    {
        var p = await db.PedidosPanico.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw NotFound();
        if (!contactOnly && p.TitularId == user) return p;
        if (!await db.DespachosPanico.AnyAsync(d => d.PedidoId == id && d.ContatoId == user &&
            (p.TitularId == null || db.Vinculos.Any(v => v.Id == d.VinculoId && v.TitularId == p.TitularId && v.ContatoId == user && v.Status == StatusVinculo.Aceito)), ct)) throw NotFound();
        return p;
    }
    private async Task<ResumoDespachos> Resumo(Guid id, CancellationToken ct)
    {
        var rows = await db.DespachosPanico.AsNoTracking().Where(x => x.PedidoId == id).ToListAsync(ct);
        int pending = rows.Count(x => x.Estado == "pendente"), expo = rows.Count(x => x.Estado is "aceito_expo" or "aceito_provedor" or "sem_recibo"),
            provider = rows.Count(x => x.Estado == "aceito_provedor"), opened = rows.Count(x => x.AbertoEm != null), failed = rows.Count(x => x.Estado is "falho" or "cancelado");
        var state = opened > 0 ? "aberto_no_app" : provider > 0 ? "aceito_provedor_push" : expo > 0 ? "aceito_expo" :
            pending > 0 ? "em_processamento" : rows.Count == 0 ? "sem_destinatarios" : "nenhum_envio_confirmado";
        return new(state, rows.Count, pending, expo, provider, opened, failed);
    }
    private static RequestException NotFound() => new("PEDIDO_NAO_ENCONTRADO", 404);
}
