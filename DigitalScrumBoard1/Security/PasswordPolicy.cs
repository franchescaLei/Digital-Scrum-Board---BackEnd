using System.Text.RegularExpressions;

namespace DigitalScrumBoard1.Security
{
    public static class PasswordPolicy
    {
        // 8+ chars, at least: 1 lowercase, 1 uppercase, 1 digit, 1 symbol
        private static readonly Regex Policy = new(
            @"^(?=.*[a-z])(?=.*[A-Z])(?=.*\d)(?=.*[^A-Za-z0-9]).{8,}$",
            RegexOptions.Compiled
        );

        public static bool IsValid(string password)
        {
            if (string.IsNullOrWhiteSpace(password)) return false;
            return Policy.IsMatch(password);
        }
    }
}