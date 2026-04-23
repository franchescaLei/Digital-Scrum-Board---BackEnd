namespace DigitalScrumBoard1.Dtos.Lookups;

public sealed class UserLookupDto
{
    public int UserID { get; set; }
    public string DisplayName { get; set; } = string.Empty;

    public string EmailAddress { get; set; } = string.Empty;

    public int? TeamID { get; set; }
    public string? TeamName { get; set; }
}