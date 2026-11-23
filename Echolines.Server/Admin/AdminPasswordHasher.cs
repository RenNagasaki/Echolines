using System.Security.Cryptography;

namespace Echolines.Server.Admin;

/// <summary>
/// PBKDF2-SHA256 with a per-hash salt, stored as <c>pbkdf2$&lt;iterations&gt;$&lt;salt&gt;$&lt;hash&gt;</c>.
/// One account, one password, no user management — so a full identity stack would be machinery
/// without a purpose. What it must get right is the two things people get wrong by hand: a real KDF
/// instead of a bare digest, and a constant-time comparison.
/// </summary>
public static class AdminPasswordHasher
{
    private const string Prefix = "pbkdf2";
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const int DefaultIterations = 210_000; // OWASP's PBKDF2-SHA256 guidance

    public static string Hash(string password, int iterations = DefaultIterations)
    {
        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(password, salt, iterations);
        return $"{Prefix}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return false;

        var parts = stored.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix || !int.TryParse(parts[1], out var iterations))
            return false;

        byte[] salt, expected;
        try
        {
            salt = Convert.FromBase64String(parts[2]);
            expected = Convert.FromBase64String(parts[3]);
        }
        catch (FormatException)
        {
            return false;
        }

        var actual = Derive(password, salt, iterations, expected.Length);

        // Constant time: a length- or content-dependent early exit leaks the password byte by byte.
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static byte[] Derive(string password, byte[] salt, int iterations, int length = HashBytes) =>
        Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, length);
}
