using Scalar.AspNetCore;
using System.Net;
using Hoodi.Api.Configuration;
using Hoodi.Api.Errors;
using Hoodi.Api.Seguranca;
using Microsoft.AspNetCore.HttpOverrides;
using System.Threading.RateLimiting;
using Hoodi.Api.Auth;
using Hoodi.Application.Auth;
using Hoodi.Domain.Entities;
using Hoodi.Infrastructure.Auth;
using Hoodi.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddControllers().ConfigureApiBehaviorOptions(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var push = context.ActionDescriptor.RouteValues["controller"] == "Push";
        var status = push ? 422 : 400;
        return new ObjectResult(new ProblemDetails
        {
            Title = "Dados inválidos", Status = status,
            Extensions = { ["code"] = push ? "TOKEN_PUSH_INVALIDO" : "DADOS_INVALIDOS", ["traceId"] = context.HttpContext.TraceIdentifier }
        }) { StatusCode = status, ContentTypes = { "application/problem+json" } };
    };
});
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, context, ct) =>
{
    document.Info.Title = "hoodi API";
    document.Info.Version = ApiInfo.Versao;
    return Task.CompletedTask;
}));
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<LimiteEmail>();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton(services => AuthenticationKeys.CreateJwt(
    services.GetRequiredService<IConfiguration>(), builder.Environment));
builder.Services.AddSingleton<IDataProtectionProvider>(services => AuthenticationKeys.CreateDataProtection(
    services.GetRequiredService<IConfiguration>(), builder.Environment));
builder.Services.AddDbContext<HoodiDbContext>(options => options.UseSqlite(
    builder.Configuration["Database:ConnectionString"] ?? "Data Source=hoodi.local.db").AddInterceptors(new SqlitePrivacyInterceptor()));
builder.Services.AddScoped<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();
builder.Services.AddScoped<IAutenticacaoService, AutenticacaoService>();
builder.Services.AddSingleton<Hoodi.Infrastructure.Risk.H3SpatialIndex>();
builder.Services.AddSingleton<Hoodi.Infrastructure.Risk.SnapshotCellCache>();
builder.Services.AddSingleton<Hoodi.Infrastructure.Rotas.RouteScoring>();
builder.Services.AddOptions<Hoodi.Infrastructure.Rotas.RotasOptions>().Bind(builder.Configuration.GetSection("Rotas"))
    .Validate(o => Enum.IsDefined(o.FonteRisco) && Enum.IsDefined(o.SemDado), "Rotas modes must be defined enum values.").ValidateOnStart();
builder.Services.AddHttpClient<Hoodi.Application.Rotas.IProvedorRotas, Hoodi.Infrastructure.Rotas.HeigitRoutesProvider>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(15); client.MaxResponseContentBufferSize = 2 * 1024 * 1024;
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false }).RemoveAllLoggers();
builder.Services.AddSingleton<Hoodi.Application.Risk.IH3CellValidator>(s => s.GetRequiredService<Hoodi.Infrastructure.Risk.H3SpatialIndex>());
builder.Services.AddScoped<Hoodi.Application.Risk.IProducerSnapshotDecoder, Hoodi.Infrastructure.Risk.ProducerV2Decoder>();
builder.Services.AddScoped<Hoodi.Application.Risk.ISnapshotPublicationStore, Hoodi.Infrastructure.Risk.SnapshotPublicationStore>();
builder.Services.AddScoped<Hoodi.Application.Risk.SnapshotImportService>();
builder.Services.AddOptions<Hoodi.Infrastructure.Risk.RiskSnapshotOptions>().Bind(builder.Configuration.GetSection("Risk"))
    .Validate(o => new[] { o.ApprovedCsvSha256, o.ApprovedManifestSha256, o.ApprovedFixtureSha256 }.All(x =>
        x is not null && System.Text.RegularExpressions.Regex.IsMatch(x, "\\A[0-9a-f]{64}\\z")), "Risk approved hashes must be lowercase SHA-256 values.")
    .ValidateOnStart();
builder.Services.AddScoped<Hoodi.Application.Conta.IContaService, Hoodi.Infrastructure.Conta.ContaService>();
builder.Services.AddScoped<Hoodi.Application.Panico.IPanicoService, Hoodi.Infrastructure.Panico.PanicoService>();
builder.Services.AddScoped<Hoodi.Infrastructure.Panico.PanicoDispatchProcessor>();
builder.Services.AddHttpClient<Hoodi.Infrastructure.Panico.IExpoGateway, Hoodi.Infrastructure.Panico.ExpoGateway>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(10);
    client.MaxResponseContentBufferSize = 1024 * 1024;
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddHostedService<Hoodi.Api.Panico.PanicoWorker>();
builder.Services.AddOptions<Hoodi.Infrastructure.Panico.PanicoOptions>().Bind(builder.Configuration.GetSection("Panico"))
    .Validate(o => o.RetencaoLocalizacaoHoras is null or >= 1 and <= 720, "Panico:RetencaoLocalizacaoHoras must be 1..720 when configured.")
    .Validate(o => o.RetencaoTitularHoras is >= 1 and <= 720, "Panico:RetencaoTitularHoras must be 1..720.")
    .ValidateOnStart();
builder.Services.AddScoped<Hoodi.Application.Contatos.IContatosService, Hoodi.Infrastructure.Contatos.ContatosService>();
builder.Services.AddScoped<Hoodi.Application.Push.IPushTokensService, Hoodi.Infrastructure.Push.PushTokensService>();
builder.Services.AddOptions<Hoodi.Infrastructure.Contatos.ContatosOptions>()
    .Bind(builder.Configuration.GetSection("Contatos"))
    .Validate(options => options.ValidadeConviteMinutos is >= 1 and <= 10080, "Contatos:ValidadeConviteMinutos must be between 1 and 10080.")
    .ValidateOnStart();

builder.Services.AddSingleton<IEmissorAccessToken>(services => services.GetRequiredService<EmissorJwt>());
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<EmissorJwt>((options, jwt) =>
{
    options.MapInboundClaims = false;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true, ValidIssuer = EmissorJwt.Issuer,
        ValidateAudience = true, ValidAudience = EmissorJwt.Audience,
        ValidateIssuerSigningKey = true, IssuerSigningKey = jwt.Key,
        ValidateLifetime = true, ClockSkew = TimeSpan.Zero,
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
    };
    options.Events = new JwtBearerEvents
    {
        OnTokenValidated = async context =>
        {
            var principal = context.Principal!;
            if (!Guid.TryParse(principal.FindFirst("sub")?.Value, out var usuarioId) ||
                !Guid.TryParse(principal.FindFirst("sid")?.Value, out var sessaoId) ||
                !await context.HttpContext.RequestServices.GetRequiredService<IAutenticacaoService>()
                    .ValidarSessaoAsync(usuarioId, sessaoId, principal.FindFirst("security_stamp")?.Value ?? "", context.HttpContext.RequestAborted))
                context.Fail("Invalid session.");
        }
    };
});
builder.Services.AddAuthorization();
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.ForwardLimit = 1;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
    options.KnownProxies.Add(IPAddress.Loopback);
    options.KnownProxies.Add(IPAddress.IPv6Loopback);
});
// CORS fechado por padrão (o app nativo não usa CORS). Para liberar um cliente web: Cors__AllowedOrigins__0=https://...
builder.Services.AddCors();
builder.Services.AddOptions<Microsoft.AspNetCore.Cors.Infrastructure.CorsOptions>().Configure<IConfiguration>((options, configuration) =>
{
    var origens = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    options.AddDefaultPolicy(policy =>
    {
        if (origens.Length > 0) policy.WithOrigins(origens).WithMethods("GET", "POST", "DELETE").WithHeaders("Authorization", "Content-Type");
    });
});
builder.Services.AddHttpsRedirection(options => options.HttpsPort = builder.Configuration.GetValue("Https:Port", 443));
builder.Services.AddRateLimiter(options =>
{
    options.AddPolicy("rotas", context => RateLimitPartition.GetFixedWindowLimiter(context.User.FindFirst("sub")?.Value ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 15, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("risco-publico", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("convites-emitir", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 5, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("convites-resgatar", context => RateLimitPartition.GetFixedWindowLimiter(
        context.User.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // Renovação de sessão: mais restrita que o limite geral de "auth", para dificultar tentativa com refresh token roubado.
    options.AddPolicy("auth-renovar", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 15, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.StatusCode = 429;
        await context.HttpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Title = "Limite de tentativas", Status = 429,
            Extensions = { ["code"] = "LIMITE_TENTATIVAS", ["traceId"] = context.HttpContext.TraceIdentifier }
        }, options: null, contentType: "application/problem+json", cancellationToken: ct);
    };
});
if (!builder.Environment.IsEnvironment("Testing")) builder.Services.AddHostedService<EmailOutboxWorker>();
// Avoid request logs containing routes, query strings or authentication data.
builder.Logging.AddFilter("Microsoft.AspNetCore", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Warning);

var app = builder.Build();
// Validate required deployment configuration before accepting requests or applying migrations.
_ = app.Services.GetRequiredService<EmissorJwt>();
_ = app.Services.GetRequiredService<IDataProtectionProvider>();
await using (var scope = app.Services.CreateAsyncScope())
    await scope.ServiceProvider.GetRequiredService<HoodiDbContext>().Database.MigrateAsync();
app.UseForwardedHeaders();
app.UseMiddleware<ApiExceptionMiddleware>();
app.UseMiddleware<CabecalhosSegurancaMiddleware>();
// Documentação interativa: ligada por padrão (demo da GS). Em produção real: Docs__Enabled=false.
if (app.Configuration.GetValue("Docs:Enabled", true))
{
    app.MapOpenApi();
    app.MapScalarApiReference("/scalar", options => options
        .WithTitle("hoodi API")
        .WithOpenApiRoutePattern("/openapi/{documentName}.json"));
}
if (app.Configuration.GetValue("Https:RedirectEnabled", true)) app.UseHttpsRedirection();
app.UseCors();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();
app.MapControllers();
app.MapGet("/health", async (HoodiDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ok", versaoApi = ApiInfo.Versao }) : Results.StatusCode(503));
app.Run();

public partial class Program;











