using System.Net;
using System.Net.Mail;

namespace medxbackend.Services;

public sealed class SmtpEmailService : IEmailService
{
    private readonly IConfiguration _configuration;

    public SmtpEmailService(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task SendPasswordResetEmailAsync(
        string email,
        string resetToken,
        CancellationToken cancellationToken = default)
    {
        var host = _configuration["Smtp:Host"]
            ?? throw new InvalidOperationException("Smtp:Host is not configured.");
        var port = _configuration.GetValue<int>("Smtp:Port");
        var username = _configuration["Smtp:Username"];
        var password = _configuration["Smtp:Password"];
        var from = _configuration["Smtp:From"]
            ?? throw new InvalidOperationException("Smtp:From is not configured.");
        var resetUrl = _configuration["PasswordReset:Url"]
            ?? "http://localhost:5173/reset-password";

        using var client = new SmtpClient(host, port == 0 ? 587 : port)
        {
            EnableSsl = true,
            Credentials = string.IsNullOrWhiteSpace(username)
                ? CredentialCache.DefaultNetworkCredentials
                : new NetworkCredential(username, password)
        };
        using var message = new MailMessage(from, email)
        {
            Subject = "密码重置",
            Body = $"请使用以下链接重置密码：{resetUrl}?token={Uri.EscapeDataString(resetToken)}"
        };

        cancellationToken.ThrowIfCancellationRequested();
        await client.SendMailAsync(message, cancellationToken);
    }
}