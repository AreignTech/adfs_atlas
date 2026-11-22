using System.Security.Cryptography;
using System.Text;
using Atlas.Application.Interfaces;
using Org.BouncyCastle.Crypto.Generators;

namespace Atlas.Infrastructure.Security;

/// <summary>
/// Verifies and produces Better Auth compatible password hashes:
/// scrypt(N=16384, r=16, p=1, dkLen=64), NFKC-normalised password, a 32-char
/// hex salt used as ASCII bytes, stored as "hexSalt:hexKey".
///
/// Matching the existing algorithm means users created under Better Auth can
/// sign in through the ASP.NET Core backend without resetting passwords.
/// </summary>
public sealed class BetterAuthPasswordHasher : IPasswordHasher
{
    private const int N = 16384;
    private const int R = 16;
    private const int P = 1;
    private const int KeyLength = 64;

    public string Hash(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        var saltBytes = RandomNumberGenerator.GetBytes(16);
        var salt = Convert.ToHexStringLower(saltBytes);
        var key = Derive(password, salt);
        return $"{salt}:{Convert.ToHexStringLower(key)}";
    }

    public bool Verify(string? hash, string password)
    {
        if (string.IsNullOrWhiteSpace(hash) || password is null)
        {
            return false;
        }

        var separator = hash.IndexOf(':');
        if (separator != 32 || hash.Length != 161 || !hash[..32].All(Uri.IsHexDigit))
        {
            return false;
        }

        var salt = hash[..separator];
        var expected = DecodeHex(hash[(separator + 1)..]);
        if (expected is null)
        {
            return false;
        }

        var actual = Derive(password, salt);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(string password, string salt)
    {
        var passwordBytes = Encoding.UTF8.GetBytes(password.Normalize(NormalizationForm.FormKC));
        var saltBytes = Encoding.UTF8.GetBytes(salt);
        return SCrypt.Generate(passwordBytes, saltBytes, N, R, P, KeyLength);
    }

    private static byte[]? DecodeHex(string value)
    {
        if (value.Length % 2 != 0)
        {
            return null;
        }

        try
        {
            return Convert.FromHexString(value);
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
