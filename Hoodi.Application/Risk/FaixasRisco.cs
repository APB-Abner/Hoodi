namespace Hoodi.Application.Risk;

public sealed record FaixaRisco(int Id, string Nome, int InicioHora, int FimHora)
{
    public string Intervalo => $"[{InicioHora},{FimHora})";
}
public static class FaixasRisco
{
    public const string Fuso = "America/Sao_Paulo";
    public static IReadOnlyList<FaixaRisco> Todas { get; } = Array.AsReadOnly(new[] {
        new FaixaRisco(0, "madrugada", 0, 6), new FaixaRisco(1, "manha", 6, 12),
        new FaixaRisco(2, "tarde", 12, 18), new FaixaRisco(3, "noite_cedo", 18, 21), new FaixaRisco(4, "noite", 21, 24) });
    public static int DaPartida(DateTimeOffset partida)
    {
        var local = TimeZoneInfo.ConvertTime(partida, TimeZoneInfo.FindSystemTimeZoneById(Fuso));
        return Todas.Single(f => local.Hour >= f.InicioHora && local.Hour < f.FimHora).Id;
    }
}
