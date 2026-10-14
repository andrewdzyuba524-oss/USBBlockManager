using System.IO;

namespace USBBlockManager.Security;

/// <summary>
/// Сервис аутентификации администратора.
/// Хранит хеш пароля в файле, проверяет ввод, поддерживает смену.
/// </summary>
public class AuthService
{
    private readonly string _hashFilePath;

    public AuthService(string hashFilePath)
    {
        _hashFilePath = hashFilePath ?? throw new ArgumentNullException(nameof(hashFilePath));
    }

    public bool IsPasswordSet => File.Exists(_hashFilePath)
                                 && PasswordManager.IsValidHashFormat(ReadHashFile());

    public void SetInitialPassword(string password)
    {
        if (IsPasswordSet)
            throw new InvalidOperationException(
                "Пароль уже установлен. Используйте ChangePassword.");

        WriteHash(password);
    }

    public bool ChangePassword(string currentPassword, string newPassword)
    {
        if (!VerifyPassword(currentPassword))
            return false;

        WriteHash(newPassword);
        return true;
    }

    public bool VerifyPassword(string password)
    {
        var stored = ReadHashFile();
        if (string.IsNullOrEmpty(stored))
            return false;

        return PasswordManager.Verify(password, stored);
    }

    private void WriteHash(string password)
    {
        var hash = PasswordManager.Hash(password);
        var dir = Path.GetDirectoryName(_hashFilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(_hashFilePath, hash);
    }

    private string? ReadHashFile()
    {
        try
        {
            return File.Exists(_hashFilePath)
                ? File.ReadAllText(_hashFilePath).Trim()
                : null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}