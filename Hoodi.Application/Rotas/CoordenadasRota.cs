using Hoodi.Application.Errors;

namespace Hoodi.Application.Rotas;

public static class CoordenadasRota
{
    public static void Validar(PontoRota p)
    {
        if (!double.IsFinite(p.Latitude) || !double.IsFinite(p.Longitude) || p.Latitude is < -90 or > 90 || p.Longitude is < -180 or >= 180)
            throw new RequestException("ROTA_INVALIDA", 422);
    }
}
