using System.Net;
using System.Net.Mail;

namespace ControlPlane.Api.Email;

public sealed class SmtpEmailSender(IConfiguration config, ILogger<SmtpEmailSender> logger)
{
    public async Task TrySendAsync(string to, string subject, string body, CancellationToken ct)
    {
        var enabled = bool.TryParse(config["ControlPlane:Smtp:Enabled"], out var e) && e;
        if (!enabled)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(to))
        {
            return;
        }

        var host = config["ControlPlane:Smtp:Host"] ?? string.Empty;
        if (string.IsNullOrWhiteSpace(host))
        {
            return;
        }

        var from = config["ControlPlane:Smtp:From"] ?? "backup@webstation.local";
        var port = int.TryParse(config["ControlPlane:Smtp:Port"], out var p) ? p : 587;
        var enableSsl = bool.TryParse(config["ControlPlane:Smtp:EnableSsl"], out var ssl) && ssl;
        var username = config["ControlPlane:Smtp:Username"] ?? string.Empty;
        var password = config["ControlPlane:Smtp:Password"] ?? string.Empty;

        try
        {
            using var client = new SmtpClient(host, port)
            {
                EnableSsl = enableSsl,
                DeliveryMethod = SmtpDeliveryMethod.Network
            };

            if (!string.IsNullOrWhiteSpace(username))
            {
                client.Credentials = new NetworkCredential(username, password);
            }

            using var msg = new MailMessage(from, to, subject, body);
            await client.SendMailAsync(msg, ct);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falha ao enviar e-mail smtp");
        }
    }
}

