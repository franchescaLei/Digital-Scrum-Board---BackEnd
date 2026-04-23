namespace DigitalScrumBoard1.DTOs.SignalR;

/// <summary>
/// Audit log entry for real-time broadcast to admins.
/// Allows frontend to prepend new entries without refetching.
/// </summary>
public sealed class AuditLogBroadcastDto
{
    public int LogID { get; set; }
    public int UserID { get; set; }
    public string Action { get; set; } = string.Empty;
    public string IPAddress { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public bool Success { get; set; }
    public string? Details { get; set; }
    public string? TargetType { get; set; }
    public int? TargetID { get; set; }
}
