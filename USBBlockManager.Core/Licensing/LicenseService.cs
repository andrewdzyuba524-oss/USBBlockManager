using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace USBBlockManager.Licensing;

/// <summary>
/// Проверка лицензий: загрузка, проверка подписи ECDsa, срока действия,
/// привязки к Hardware ID.
///
/// Публичный ключ зашит в константу <see cref="PublicKeyBase64"/>.
/// Приватный ключ хранится у издателя (в LicenseGenerator) и никогда
/// не попадает в продукт.
/// </summary>
public static class LicenseService
{
    /// <summary>
    /// Публичный ECDsa-ключ (Base64, SubjectPublicKeyInfo).
    /// Заполняется после генерации ключей — Этап 3.
    /// Пока пусто — проверка подписи вернёт PublicKeyNotConfigured.
    /// </summary>
    public const string PublicKeyBase64 = "MFkwEwYHKoZIzj0CAQYIKoZIzj0DAQcDQgAELDbMKAvDfh4xg2qdbgYwGcvm4bijqnTYJuDfSBtLngXoU+/1HZGd5vhysFkOzCtDARUHHlHH1k8ESkFCpFJPiQ==";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Загрузить лицензию из файла и проверить её.
    /// </summary>
    public static LicenseValidationResult ValidateFromFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return LicenseValidationResult.Fail(
                LicenseValidationError.FileNotFound,
                "Путь к файлу лицензии не указан.");

        if (!File.Exists(filePath))
            return LicenseValidationResult.Fail(
                LicenseValidationError.FileNotFound,
                $"Файл лицензии не найден: {filePath}");

        try
        {
            var json = File.ReadAllText(filePath);
            return ValidateFromJson(json);
        }
        catch (IOException ex)
        {
            return LicenseValidationResult.Fail(
                LicenseValidationError.FileReadError,
                $"Ошибка чтения файла лицензии: {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            return LicenseValidationResult.Fail(
                LicenseValidationError.FileReadError,
                $"Нет доступа к файлу лицензии: {ex.Message}");
        }
    }

    /// <summary>
    /// Проверить лицензию из строки JSON.
    /// </summary>
    public static LicenseValidationResult ValidateFromJson(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return LicenseValidationResult.Fail(
                LicenseValidationError.InvalidFormat,
                "Лицензия пуста.");

        License? license;
        try
        {
            license = JsonSerializer.Deserialize<License>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            return LicenseValidationResult.Fail(
                LicenseValidationError.InvalidFormat,
                $"Не удалось разобрать JSON лицензии: {ex.Message}");
        }

        if (license is null)
            return LicenseValidationResult.Fail(
                LicenseValidationError.InvalidFormat,
                "Лицензия пуста или не распознана.");

        // 1. Проверка публичного ключа
        if (string.IsNullOrWhiteSpace(PublicKeyBase64))
            return LicenseValidationResult.Fail(
                LicenseValidationError.PublicKeyNotConfigured,
                "Публичный ключ не настроен в программе.",
                license);

        // 2. Проверка подписи
        if (string.IsNullOrWhiteSpace(license.Signature))
            return LicenseValidationResult.Fail(
                LicenseValidationError.InvalidSignature,
                "В лицензии отсутствует подпись.",
                license);

        if (!VerifySignature(license, json))
            return LicenseValidationResult.Fail(
                LicenseValidationError.InvalidSignature,
                "Подпись лицензии недействительна. Возможно, файл был изменён.",
                license);

        // 3. Проверка срока действия
        if (license.IsExpired)
            return LicenseValidationResult.Fail(
                LicenseValidationError.Expired,
                $"Срок действия лицензии истёк {license.ExpiresAt:yyyy-MM-dd}.",
                license);

        // 4. Проверка Hardware ID
        string currentId;
        try
        {
            currentId = HardwareId.Get();
        }
        catch (Exception ex)
        {
            return LicenseValidationResult.Fail(
                LicenseValidationError.HardwareMismatch,
                $"Не удалось получить Hardware ID: {ex.Message}",
                license);
        }

        if (!string.IsNullOrWhiteSpace(license.HardwareId)
            && !string.Equals(license.HardwareId, currentId, StringComparison.OrdinalIgnoreCase))
        {
            return LicenseValidationResult.Fail(
                LicenseValidationError.HardwareMismatch,
                $"Лицензия привязана к другому компьютеру.\n" +
                $"Ожидаемый: {license.HardwareId}\n" +
                $"Текущий:   {currentId}",
                license,
                currentId);
        }

        // Всё ок
        return LicenseValidationResult.Ok(license, currentId);
    }

    /// <summary>
    /// Проверить ECDsa-подпись лицензии.
    /// Подпись вычисляется на canonical-представлении JSON без поля signature.
    /// </summary>
    private static bool VerifySignature(License license, string originalJson)
    {
        try
        {
            var canonical = CanonicalizeForSigning(originalJson);
            var dataBytes = Encoding.UTF8.GetBytes(canonical);
            var signatureBytes = Convert.FromBase64String(license.Signature);

            using var ecdsa = ECDsa.Create();
            ecdsa.ImportSubjectPublicKeyInfo(
                Convert.FromBase64String(PublicKeyBase64),
                out _);

            return ecdsa.VerifyData(
                dataBytes,
                signatureBytes,
                HashAlgorithmName.SHA256,
                DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Преобразует JSON лицензии в canonical-форму для подписи:
    /// убирает поле "signature", нормализует пробелы.
    /// Один и тот же алгоритм используется и при выпуске, и при проверке.
    /// </summary>
    public static string CanonicalizeForSigning(string json)
    {
        var node = JsonNode.Parse(json)
            ?? throw new InvalidOperationException("Не удалось разобрать JSON.");

        if (node is JsonObject obj)
            obj.Remove("signature");

        // JsonSerializer с WriteIndented=false даёт компактный детерминированный вид
        var options = new JsonSerializerOptions
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        return node.ToJsonString(options);
    }

    // ----- методы для утилиты генерации лицензий (Этап 3) -----

    /// <summary>
    /// Подписать лицензию приватным ключом (используется LicenseGenerator).
    /// </summary>
    public static string SignLicense(License license, ECDsa privateKey)
    {
        var json = JsonSerializer.Serialize(license, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        var canonical = CanonicalizeForSigning(json);
        var dataBytes = Encoding.UTF8.GetBytes(canonical);

        var signature = privateKey.SignData(
            dataBytes,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        return Convert.ToBase64String(signature);
    }
}