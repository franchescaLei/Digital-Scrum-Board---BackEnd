namespace DigitalScrumBoard1.Services
{
    public interface IAuditLogService
    {
        Task<object?> GetByIdAsync(int id, CancellationToken ct);
        Task<object> GetPagedAsync(
            int? userId, string? action, bool? success, DateTime? from, DateTime? to,
            string? targetType, int? targetId, string? ipAddress,
            int page, int pageSize, CancellationToken ct);

        Task<(byte[] bytes, string fileName)> ExportCsvAsync(
            int? userId, string? action, bool? success, DateTime? from, DateTime? to,
            string? targetType, int? targetId, string? ipAddress,
            CancellationToken ct);
    }
}