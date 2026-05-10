using System;
using System.Globalization;
using System.Security.Cryptography;

namespace FNS_graphics.Data
{
    internal static class Password_hasher
    {
        private const string Algorithm = "pbkdf2-sha256";
        private const int SaltSizeBytes = 16;
        private const int HashSizeBytes = 32;
        private const int Iterations = 100_000;

        internal static string HashPassword(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSizeBytes);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                HashSizeBytes);

            return string.Join(
                '$',
                Algorithm,
                Iterations.ToString(CultureInfo.InvariantCulture),
                Convert.ToBase64String(salt),
                Convert.ToBase64String(hash));
        }

        internal static bool VerifyPassword(string password, string storedHash)
        {
            string[] parts = storedHash.Split('$');
            if (parts.Length != 4 || parts[0] != Algorithm)
                return false;

            if (!int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out int iterations))
                return false;

            byte[] salt;
            byte[] expectedHash;
            try
            {
                salt = Convert.FromBase64String(parts[2]);
                expectedHash = Convert.FromBase64String(parts[3]);
            }
            catch (FormatException)
            {
                return false;
            }

            byte[] actualHash = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                iterations,
                HashAlgorithmName.SHA256,
                expectedHash.Length);

            return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
        }
    }
}
