using Hoodi.Application.Auth;
using Hoodi.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;

namespace Hoodi.Api.Errors;

public sealed class ApiExceptionMiddleware(RequestDelegate next, ILogger<ApiExceptionMiddleware> logger)
{
    public async Task InvokeAsync(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        try { await next(context); }
        catch (Exception ex) when (!context.RequestAborted.IsCancellationRequested && !context.Response.HasStarted)
        {
            var (status, code, title) = ex switch
            {
                Hoodi.Application.Risk.SnapshotValidationException snapshot =>
                    (snapshot.Code == "VERSAO_CONFLITANTE" ? 409 : 422, snapshot.Code, "Snapshot inválido"),
                Hoodi.Application.Errors.RequestException request => (request.Status, request.Code, "Solicitação inválida"),
                AutenticacaoException auth => (auth.Status, auth.Code,
                    auth.Status == 429 ? "Limite de tentativas" : "Solicitação inválida"),
                DomainException domain when domain.Code == "VINCULO_PROPRIO" =>
                    (StatusCodes.Status422UnprocessableEntity, domain.Code, "Vínculo próprio não permitido"),
                DomainException domain => (StatusCodes.Status409Conflict, domain.Code, "Conflito com regra de negócio"),
                _ => (StatusCodes.Status500InternalServerError, "ERRO_INTERNO", "Erro interno")
            };
            // Exception messages and payloads can contain PII. Log only safe identifiers.
            if (status == 500)
                logger.LogError("Unhandled API error. Exception type: {ExceptionType}; trace: {TraceId}", ex.GetType().Name, context.TraceIdentifier);
            context.Response.StatusCode = status;
            await context.Response.WriteAsJsonAsync(new ProblemDetails
            {
                Title = title, Status = status,
                Extensions = { ["code"] = code, ["traceId"] = context.TraceIdentifier }
            }, options: null, contentType: "application/problem+json", cancellationToken: context.RequestAborted);
        }
    }
}




