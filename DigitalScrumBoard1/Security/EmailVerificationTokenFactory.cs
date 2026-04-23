using System.Security.Cryptography;
using System.Text;

namespace DigitalScrumBoard1.Security
{
    public static class EmailVerificationTokenFactory
    {
        public static string CreateRawToken()
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            return Convert.ToBase64String(bytes);
        }

        public static string CreateSixDigitCode()
        {
            return RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");
        }

        public static string HashToken(string rawToken)
        {
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(rawToken));
            return Convert.ToBase64String(hash);
        }
    }
}