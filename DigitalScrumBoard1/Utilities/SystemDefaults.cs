namespace DigitalScrumBoard1.Utilities;

/// <summary>
/// System-wide constants for default/fallback records.
/// </summary>
public static class SystemDefaults
{
    /// <summary>
    /// TeamID 1 represents "Default Team" which is semantically "unassigned".
    /// Users/work items/sprints with this ID should be treated as having no team.
    /// </summary>
    public const int DefaultTeamId = 1;
}
