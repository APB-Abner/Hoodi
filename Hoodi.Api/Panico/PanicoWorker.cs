using Hoodi.Infrastructure.Panico;
using Microsoft.Extensions.Options;

namespace Hoodi.Api.Panico;

public sealed class PanicoWorker(IServiceScopeFactory scopes, ILogger<PanicoWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                await scope.ServiceProvider.GetRequiredService<PanicoDispatchProcessor>().ProcessAsync(
                    scope.ServiceProvider.GetRequiredService<IOptions<PanicoOptions>>().Value.EnvioExpoHabilitado, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception ex) { logger.LogError("Panic worker failed. Type: {Type}", ex.GetType().Name); }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}
