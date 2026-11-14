namespace Hoodi.Api.Auth;

public static class AuthenticationEmailBody
{
    public static string Create(string purpose, string token)
    {
        var action = purpose switch
        {
            "confirmar_email" => "confirmar",
            "reset_senha" => "redefinir-senha",
            _ => throw new ArgumentException("Unsupported authentication email purpose.", nameof(purpose))
        };
        // A URI fragment is processed by the app and is not part of an HTTP request target.
        var link = $"hoodi://{action}#token={Uri.EscapeDataString(token)}";
        return "Abra o link no app hoodi. Validade: 30 minutos.\n\n" + link;
    }
}
