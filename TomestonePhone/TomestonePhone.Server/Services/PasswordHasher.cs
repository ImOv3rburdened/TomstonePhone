using System.Security.Cryptography;

namespace TomestonePhone.Server.Services;

public static class PasswordHasher
{
    public static string Hash(string password, string salt)
    {
        var derived = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(salt), 100_000, HashAlgorithmName.SHA512, 32);
        return Convert.ToBase64String(derived);
    }

    public static bool Verify(string password, string salt, string expectedHash)
    {
        var actual = Hash(password, salt);
        return CryptographicOperations.FixedTimeEquals(Convert.FromBase64String(actual), Convert.FromBase64String(expectedHash));
    }
}
