namespace DigitalScrumBoard1.Services
{
    public interface IAuditService
    {
        Task LogAsync(
            int actorUserId,
            string action,
            string targetType,
            int targetId,
            bool success,
            string details,
            string ipAddress,
            CancellationToken ct = default);
    }
}