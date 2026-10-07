namespace medxbackend.Services;

public interface IEmailService
{
    Task SendPasswordResetEmailAsync(
        string email,
        string resetToken,
        CancellationToken cancellationToken = default);
}