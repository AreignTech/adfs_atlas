using System.Security.Cryptography;
using System.Text;
using Atlas.Application.Interfaces;
using Microsoft.Extensions.Configuration;

namespace Atlas.Infrastructure.Security;

public sealed class LdapSecretProtector(IConfiguration configuration) : ISecretProtector
{
    private string CurrentKey => new[] { configuration["LDAP_CONFIG_ENCRYPTION_KEY"], configuration["Ldap:EncryptionKey"], configuration["BETTER_AUTH_SECRET"] }.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value)) ?? throw new InvalidOperationException("LDAP encryption key is not configured.");
    private static byte[] Key(string value) => SHA256.HashData(Encoding.UTF8.GetBytes(value));
    public string Protect(string value)
    {
        if (value.Length == 0) return "";
        var nonce = RandomNumberGenerator.GetBytes(12); var tag = new byte[16]; var plain = Encoding.UTF8.GetBytes(value); var cipher = new byte[plain.Length];
        using var aes = new AesGcm(Key(CurrentKey), 16); aes.Encrypt(nonce, plain, cipher, tag); CryptographicOperations.ZeroMemory(plain);
        return $"{Convert.ToBase64String(nonce)}.{Convert.ToBase64String(tag)}.{Convert.ToBase64String(cipher)}";
    }
    public string Unprotect(string value)
    {
        if (value.Length == 0) return "";
        var parts = value.Split('.'); if (parts.Length != 3) throw new CryptographicException("Invalid LDAP secret envelope.");
        var keys = new[] { CurrentKey }.Concat((configuration["Ldap:PreviousEncryptionKeys"] ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries));
        foreach (var key in keys)
        {
            try
            {
                var cipher = Convert.FromBase64String(parts[2]); var plain = new byte[cipher.Length];
                using var aes = new AesGcm(Key(key), 16); aes.Decrypt(Convert.FromBase64String(parts[0]), cipher, Convert.FromBase64String(parts[1]), plain);
                var result = Encoding.UTF8.GetString(plain); CryptographicOperations.ZeroMemory(plain); return result;
            }
            catch (CryptographicException) { }
        }
        throw new CryptographicException("LDAP secret could not be decrypted with the configured keys.");
    }
}
