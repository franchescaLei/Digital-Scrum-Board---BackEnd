using DigitalScrumBoard1.Models;

namespace DigitalScrumBoard1.Services
{
    public interface IAuthEmailService
    {
        Task SendWelcomeAndVerificationAsync(User user, string temporaryPassword, CancellationToken ct);
        Task SendVerificationAsync(User user, CancellationToken ct);
        Task SendPasswordResetCodeAsync(User user, string code, int expiresInSeconds, CancellationToken ct);
    }
}