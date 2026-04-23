namespace DigitalScrumBoard1.Security
{
    using System.Security.Cryptography;

    public static class PasswordGenerator
    {
        // 14 chars is a good default for temp password
        public static string Generate(int length = 14)
        {
            if (length < 12) length = 12;

            const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ"; // removed I,O to avoid confusion
            const string lower = "abcdefghijkmnpqrstuvwxyz"; // removed l,o
            const string digits = "23456789";                 // removed 0,1
            const string symbols = "!@#$%^&*_-+=";

            var all = upper + lower + digits + symbols;

            // ensure at least one from each class
            Span<char> pwd = stackalloc char[length];
            pwd[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
            pwd[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
            pwd[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
            pwd[3] = symbols[RandomNumberGenerator.GetInt32(symbols.Length)];

            for (int i = 4; i < length; i++)
                pwd[i] = all[RandomNumberGenerator.GetInt32(all.Length)];

            // shuffle
            for (int i = pwd.Length - 1; i > 0; i--)
            {
                int j = RandomNumberGenerator.GetInt32(i + 1);
                (pwd[i], pwd[j]) = (pwd[j], pwd[i]);
            }

            return new string(pwd);
        }
    }
}