using System.Net.Mail;
using System.Text.Json;
using Hoodi.Infrastructure.Auth;
using Hoodi.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Hoodi.Api.Auth;

public sealed class EmailOutboxWorker(IServiceScopeFactory scopes, IConfiguration config, ILogger<EmailOutboxWorker> logger)
    : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<HoodiDbContext>();
                var protector = scope.ServiceProvider.GetRequiredService<IDataProtectionProvider>()
                    .CreateProtector(Hoodi.Application.Auth.ProtectionPurposes.AuthenticationEmailOutbox);
                var clock = scope.ServiceProvider.GetRequiredService<TimeProvider>();
                var agora = clock.GetUtcNow();
                // Remove expired replay ciphertext instead of keeping refresh tokens indefinitely.
                var usados = await db.RefreshTokens.Where(x => x.ReplayCifrado != null && x.UsadoEmUtc != null).ToListAsync(stoppingToken);
                foreach (var usado in usados.Where(x => x.UsadoEmUtc!.Value.AddSeconds(45) <= agora)) usado.ReplayCifrado = null;
                await db.SaveChangesAsync(stoppingToken);
                var pendentes = await db.Emails.Where(x => x.EnviadoEmUtc == null).Take(20).ToListAsync(stoppingToken);
                foreach (var item in pendentes)
                {
                    if (agora - item.CriadoEmUtc >= TimeSpan.FromMinutes(30)) { db.Emails.Remove(item); continue; }
                    EmailAutenticacao mensagem;
                    try { mensagem = JsonSerializer.Deserialize<EmailAutenticacao>(protector.Unprotect(item.ConteudoCifrado))!; }
                    catch (System.Security.Cryptography.CryptographicException)
                    {
                        logger.LogError("Authentication outbox cannot be decrypted. Restore the configured key ring; message retained.");
                        continue;
                    }
                    using var mail = new MailMessage("hoodi@example.invalid", mensagem.Email,
                        mensagem.Proposito == "confirmar_email" ? "hoodi — Confirme seu e-mail" : "hoodi — Redefina sua senha",
                        AuthenticationEmailBody.Create(mensagem.Proposito, mensagem.Token));
                    using var smtp = new SmtpClient(config["Smtp:Host"] ?? "127.0.0.1", config.GetValue("Smtp:Port", 1025));
                    await smtp.SendMailAsync(mail, stoppingToken);
                    db.Emails.Remove(item);
                    await db.SaveChangesAsync(stoppingToken);
                }
                await db.SaveChangesAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception)
            {
                // SMTP exceptions can contain recipients; never log exception messages or payloads.
                logger.LogWarning("Authentication outbox delivery failed; pending messages will be retried.");
            }
            try { await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken); }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
        }
    }
}



