using Hoodi.Application.Risk;
using Hoodi.Application.Rotas;
using Hoodi.Infrastructure.Risk;
using Microsoft.Extensions.Options;

namespace Hoodi.Infrastructure.Rotas;

public sealed class GradeParaRotasReader(ISnapshotPublicationStore store, IProducerSnapshotDecoder decoder, SnapshotCellCache cache) : IGradeParaRotas
{
    public async Task<GradeParaRotas?> LerAtualAsync(CancellationToken ct)
    {
        var index = await cache.ReadIndexAsync(store, decoder, ct);
        return index is null ? null : new(index.Descriptor.Version, index.Rows);
    }
}
public sealed class PoliticaRotasReader(IOptions<RotasOptions> options) : IPoliticaRotas
{
    public PoliticaRotas Ler() => new(options.Value.FonteRisco, options.Value.SemDado);
}
