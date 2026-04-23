namespace DigitalScrumBoard1.Services;

public static class BoardWorkflowRules
{
    private static readonly Dictionary<string, HashSet<string>> AllowedTransitions =
        new()
        {
            ["To-do"] = new() { "Ongoing" },

            ["Ongoing"] = new()
            {
                "To-do",
                "For Checking"
            },

            ["For Checking"] = new()
            {
                "Ongoing",
                "Completed"
            },

            ["Completed"] = new()
            {
                "For Checking"
            }
        };

    public static bool IsValidTransition(string from, string to)
    {
        if (!AllowedTransitions.ContainsKey(from))
            return false;

        return AllowedTransitions[from].Contains(to);
    }
}