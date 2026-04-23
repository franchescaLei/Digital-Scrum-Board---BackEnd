namespace DigitalScrumBoard1.Security
{
    using System.Security.Cryptography;

    public static class PasswordHasher
    {
        // Tune if needed (higher = slower but more resistant to brute force)
        private const int SaltSizeBytes = 16;   // 128-bit salt
        private const int KeySizeBytes = 32;    // 256-bit derived key
        private const int DefaultIterations = 150_000;

        // Format: v1.<iterations>.<saltB64>.<keyB64>
        public static string Hash(string password)
        {
            if (string.IsNullOrWhiteSpace(password))
                throw new ArgumentException("Password must not be empty.", nameof(password));

            var salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);

            var key = Rfc2898DeriveBytes.Pbkdf2(
                password: password,
                salt: salt,
                iterations: DefaultIterations,
                hashAlgorithm: HashAlgorithmName.SHA256,
                outputLength: KeySizeBytes
            );

            return $"v1.{DefaultIterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
        }

        public static bool Verify(string password, string storedHash)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrWhiteSpace(storedHash))
                return false;

            // Support legacy SHA256 (your old version) for existing users if needed.
            // If you don't want legacy support, remove this block.
            if (!storedHash.StartsWith("v1.", StringComparison.Ordinal))
            {
                // Legacy format = Base64(SHA256(password))
                using var sha256 = SHA256.Create();
                var computed = Convert.ToBase64String(sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(password)));
                return FixedTimeEqualsBase64(computed, storedHash);
            }

            var parts = storedHash.Split('.', 4);
            if (parts.Length != 4) return false;

            if (!int.TryParse(parts[1], out var iterations) || iterations <= 0)
                return false;

            byte[] salt, expectedKey;
            try
            {
                salt = Convert.FromBase64String(parts[2]);
                expectedKey = Convert.FromBase64String(parts[3]);
            }
            catch (FormatException)
            {
                return false;
            }

            var actualKey = Rfc2898DeriveBytes.Pbkdf2(
                password: password,
                salt: salt,
                iterations: iterations,
                hashAlgorithm: HashAlgorithmName.SHA256,
                outputLength: expectedKey.Length
            );

            return CryptographicOperations.FixedTimeEquals(actualKey, expectedKey);
        }

        private static bool FixedTimeEqualsBase64(string aBase64, string bBase64)
        {
            try
            {
                var a = Convert.FromBase64String(aBase64);
                var b = Convert.FromBase64String(bBase64);
                return CryptographicOperations.FixedTimeEquals(a, b);
            }
            catch (FormatException)
            {
                return false;
            }
        }
    }
}