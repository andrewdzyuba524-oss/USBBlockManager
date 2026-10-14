using System.Security.Cryptography;

namespace USBBlockManager.Security;

/// <summary>
/// Хеширование и проверка пароля администратора через PBKDF2-SHA256.
/// Формат хранения: PBKDF2$iterations$saltBase64$hashBase64
/// </summary>
public static class PasswordManager
{
    private const int SaltSize = 16;          // 128 бит
    private const int HashSize = 32;          // 256 бит
    private const int DefaultIterations = 200_000;
    private const int MinIterations = 10_000;
    private const int MaxIterations = 10_000_000;  // защита от DoS
    private const string Prefix = "PBKDF2";

    public static string Hash(string password, int iterations = DefaultIterations)
    {
        if (string.IsNullOrEmpty(password))
            throw new ArgumentException("Пароль не может быть пустым.", nameof(password));

        if (iterations < MinIterations || iterations > MaxIterations)
            throw new ArgumentOutOfRangeException(nameof(iterations),
                $"Итерации должны быть в диапазоне {MinIterations}..{MaxIterations}.");

        byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
        byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
            password: password,
            salt: salt,
            iterations: iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: HashSize);

        return $"{Prefix}${iterations}${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string storedHash)
    {
        if (string.IsNullOrEmpty(password) || string.IsNullOrEmpty(storedHash))
            return false;

        var parts = storedHash.Split('$');
        if (parts.Length != 4 || parts[0] != Prefix)
            return false;

        // Защита от DoS: iterations должны быть в разумных пределах
        if (!int.TryParse(parts[1], out int iterations)
            || iterations < MinIterations
            || iterations > MaxIterations)
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
            password: password,
            salt: salt,
            iterations: iterations,
            hashAlgorithm: HashAlgorithmName.SHA256,
            outputLength: expectedHash.Length);

        return CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);
    }

    public static bool IsValidHashFormat(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var parts = value.Split('$');
        return parts.Length == 4
               && parts[0] == Prefix
               && int.TryParse(parts[1], out int it)
               && it >= MinIterations
               && it <= MaxIterations;
    }
}