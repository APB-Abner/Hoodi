using System.Globalization;
using H3;
using H3.Algorithms;
using H3.Extensions;
using Hoodi.Application.Risk;
using NetTopologySuite.Geometries;

namespace Hoodi.Infrastructure.Risk;

public sealed class H3SpatialIndex : IH3CellValidator, Hoodi.Application.Rotas.IIndiceEspacialRotas
{
    public string Cell(Hoodi.Application.Rotas.PontoRota point)
    {
        Hoodi.Application.Rotas.CoordenadasRota.Validar(point);
        return new Coordinate(point.Longitude, point.Latitude).ToH3Index(9).ToString();
    }
    public bool IsValidResolution9(string cell) => cell is { Length: 15 } &&
        ulong.TryParse(cell, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value) &&
        new H3Index(value) is var index && index.IsValidCell && index.Resolution == 9;

    public IReadOnlySet<string> Coverage(double latMin, double latMax, double lonMin, double lonMax)
    {
        var polygon = new GeometryFactory().CreatePolygon(new[] {
            new Coordinate(lonMin, latMin), new Coordinate(lonMax, latMin), new Coordinate(lonMax, latMax),
            new Coordinate(lonMin, latMax), new Coordinate(lonMin, latMin) });
        // Match the producer's overlap semantics, not centroid-only or vertex-only containment.
        return polygon.Fill(9, cell => cell.GetCellBoundary().Intersects(polygon)).Select(x => x.ToString()).ToHashSet(StringComparer.Ordinal);
    }
}
