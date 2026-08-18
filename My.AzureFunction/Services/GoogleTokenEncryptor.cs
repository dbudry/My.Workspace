using System.Security.Cryptography;
using System.Text;

namespace My.Functions.Services
{
    /// <summary>
    /// AES-GCM envelope for Google refresh tokens.
    /// Key comes from app setting Google__TokenEncryptionKey (base64, 32 bytes).
    /// Storage format: base64( nonce(12) || ciphertext(n) || tag(16) ).
    /// </summary>
    public class GoogleTokenEncryptor
    {
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const string KeyEnvVar = "Google__TokenEncryptionKey";

        private readonly Lazy<byte[]> key = new(LoadKey);

        private static byte[] LoadKey()
        {
            var raw = Environment.GetEnvironmentVariable(KeyEnvVar);
            if (string.IsNullOrWhiteSpace(raw))
                throw new InvalidOperationException(
                    $"{KeyEnvVar} is not configured. Generate 32 random bytes and store as base64.");

            byte[] decoded;
            try { decoded = Convert.FromBase64String(raw); }
            catch (FormatException) { throw new InvalidOperationException($"{KeyEnvVar} must be base64-encoded."); }

            if (decoded.Length != 32)
                throw new InvalidOperationException($"{KeyEnvVar} must decode to 32 bytes (AES-256).");

            return decoded;
        }

        public bool IsConfigured => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(KeyEnvVar));

        public string Encrypt(string plaintext)
        {
            var plainBytes = Encoding.UTF8.GetBytes(plaintext);
            var nonce = RandomNumberGenerator.GetBytes(NonceSize);
            var cipher = new byte[plainBytes.Length];
            var tag = new byte[TagSize];

            using var aes = new AesGcm(key.Value, TagSize);
            aes.Encrypt(nonce, plainBytes, cipher, tag);

            var packed = new byte[NonceSize + cipher.Length + TagSize];
            Buffer.BlockCopy(nonce, 0, packed, 0, NonceSize);
            Buffer.BlockCopy(cipher, 0, packed, NonceSize, cipher.Length);
            Buffer.BlockCopy(tag, 0, packed, NonceSize + cipher.Length, TagSize);
            return Convert.ToBase64String(packed);
        }

        public string Decrypt(string encoded)
        {
            var packed = Convert.FromBase64String(encoded);
            if (packed.Length < NonceSize + TagSize)
                throw new CryptographicException("Encrypted payload is too short.");

            var cipherLen = packed.Length - NonceSize - TagSize;
            var nonce = new byte[NonceSize];
            var cipher = new byte[cipherLen];
            var tag = new byte[TagSize];
            Buffer.BlockCopy(packed, 0, nonce, 0, NonceSize);
            Buffer.BlockCopy(packed, NonceSize, cipher, 0, cipherLen);
            Buffer.BlockCopy(packed, NonceSize + cipherLen, tag, 0, TagSize);

            var plain = new byte[cipherLen];
            using var aes = new AesGcm(key.Value, TagSize);
            aes.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
    }
}
