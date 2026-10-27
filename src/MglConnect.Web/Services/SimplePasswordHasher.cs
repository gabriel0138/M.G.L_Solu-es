using System;
using System.Security.Cryptography;
using System.Text;

namespace MglConnect.Web.Services
{
    public class SimplePasswordHasher : IPasswordHasher
    {
        private const int SaltSize = 16; // 128 bits
        private const int KeySize = 32;  // 256 bits
        private const int Iterations = 100000;

        public string HashPassword(string password)
        {
            if (string.IsNullOrEmpty(password)) return string.Empty;

            using var rng = RandomNumberGenerator.Create();
            var salt = new byte[SaltSize];
            rng.GetBytes(salt);

            var key = Rfc2898DeriveBytes.Pbkdf2(
                password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);

            var hashBytes = new byte[SaltSize + KeySize];
            Array.Copy(salt, 0, hashBytes, 0, SaltSize);
            Array.Copy(key, 0, hashBytes, SaltSize, KeySize);

            return Convert.ToBase64String(hashBytes);
        }

        public bool VerifyPassword(string password, string passwordHash)
        {
            if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(passwordHash)) return false;

            try
            {
                var hashBytes = Convert.FromBase64String(passwordHash);

                // Compatibilidade com hash antigo (SHA256 puro, 32 bytes) - validar com salt antigo apenas para migração
                if (hashBytes.Length == KeySize)
                {
                    using var sha256Legacy = SHA256.Create();
                    var legacyBytes = sha256Legacy.ComputeHash(Encoding.UTF8.GetBytes(password + "_MGL_CONNECT_SALT_2026"));
                    return CryptographicOperations.FixedTimeEquals(legacyBytes, hashBytes);
                }

                if (hashBytes.Length != SaltSize + KeySize)
                    return false;

                var salt = new byte[SaltSize];
                Array.Copy(hashBytes, 0, salt, 0, SaltSize);

                var key = new byte[KeySize];
                Array.Copy(hashBytes, SaltSize, key, 0, KeySize);

                var keyToCheck = Rfc2898DeriveBytes.Pbkdf2(
                    password, salt, Iterations, HashAlgorithmName.SHA256, KeySize);

                return CryptographicOperations.FixedTimeEquals(keyToCheck, key);
            }
            catch
            {
                return false;
            }
        }
    }
}
