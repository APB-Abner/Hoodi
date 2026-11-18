namespace Hoodi.Api.Seguranca;

/// <summary>
/// Cabeçalhos de segurança em toda resposta. A API devolve JSON (ou a página do Scalar), nunca é embutida em frame
/// e não deve ser guardada em cache de proxy: respostas contêm dados de conta e de localização.
/// </summary>
public sealed class CabecalhosSegurancaMiddleware(RequestDelegate next)
{
    public Task InvokeAsync(HttpContext context)
    {
        context.Response.OnStarting(() =>
        {
            var h = context.Response.Headers;
            h["X-Content-Type-Options"] = "nosniff";
            h["Referrer-Policy"] = "no-referrer";
            h["X-Frame-Options"] = "DENY";
            if (!h.ContainsKey("Cache-Control")) h["Cache-Control"] = "no-store";
            return Task.CompletedTask;
        });
        return next(context);
    }
}
