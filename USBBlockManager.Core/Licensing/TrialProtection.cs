using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Win32;

namespace USBBlockManager.Licensing;

/// <summary>
/// Защита trial.json: HMAC-подпись + дублирование в реестр HKLM.
/// </summary>
public static class TrialProtection
{
    /// <summary>
    /// Секрет для HMAC. Зашит в программу.
    /// После обфускации exe — достать сложнее.
    /// Можешь заменить байты на свои случайные — главное, чтобы они
    /// не попали в публичный репозиторий.
    /// </summary>
    private static readonly byte[] HmacSecret = new byte[]
    {
        0x8A, 0x3F, 0x2C, 0x91, 0x77, 0xD4, 0x5E, 0x18,
        0xB2, 0x64, 0x0F, 0xA9, 0xC1, 0x33, 0x7E, 0x52,
        0xE8, 0x91, 0x4A, 0x2D, 0xF6, 0x03, 0xB7, 0xCC,
        0x1A, 0x5D, 0x88, 0x42, 0x6F, 0xD9, 0x23, 0x70
    };

    private const string RegistryPath = @"SOFTWARE\USBBlockManager";
    private const string RegistryValueName = "TrialData";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    /// <summary>
    /// Загрузить или создать TrialInfo.
    /// Проверяет HMAC в файле и реестре, сверяет их, берёт самую раннюю дату.
    /// </summary>
    public static TrialInfo LoadOrCreate(string filePath)
    {
        var fromFile = TryLoadFromFile(filePath);
        var fromRegistry = TryLoadFromRegistry();

        // Ни того, ни другого — создаём новый триал
        if (fromFile is null && fromRegistry is null)
        {
            var fresh = new TrialInfo
            {
                FirstRunAt = DateTime.UtcNow,
                LastRunAt = DateTime.UtcNow,
                TrialDays = 7,
            };
            Save(filePath, fresh);
            return fresh;
        }

        TrialInfo chosen;

        if (fromFile is null)
        {
            // Файл повреждён/удалён — берём из реестра
            chosen = fromRegistry!;
        }
        else if (fromRegistry is null)
        {
            // Реестр повреждён/удалён — берём из файла
            chosen = fromFile;
        }
        else
        {
            // Оба источника валидны — берём САМУЮ РАННЮЮ дату firstRunAt.
            // Если пользователь откатил часы, чтобы продлить триал, — не поможет.
            chosen = fromFile.FirstRunAt <= fromRegistry.FirstRunAt
                ? fromFile
                : fromRegistry;
        }

        // Обновляем lastRunAt
        chosen.LastRunAt = DateTime.UtcNow;

        // Защита от отката часов: если lastRunAt из будущего — сдвигаем firstRunAt.
        if (chosen.LastRunAt > DateTime.UtcNow.AddHours(1))
        {
            var delta = chosen.LastRunAt - DateTime.UtcNow;
            chosen.FirstRunAt -= delta;
        }

        Save(filePath, chosen);
        return chosen;
    }

    /// <summary>
    /// Сохранить TrialInfo — в файл + в реестр + с HMAC-подписью.
    /// </summary>
    public static void Save(string filePath, TrialInfo trial)
    {
        // Считаем HMAC от canonical-представления (без поля signature)
        var canonical = BuildCanonical(trial);
        trial.Signature = ComputeHmac(canonical);

        var json = JsonSerializer.Serialize(trial, JsonOptions);

        // Файл
        try
        {
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(filePath, json);
        }
        catch
        {
            // Не критично — попробуем хотя бы реестр
        }

        // Реестр HKLM
        try
        {
            using var key = Registry.LocalMachine.CreateSubKey(RegistryPath, writable: true);
            key?.SetValue(RegistryValueName, json, RegistryValueKind.String);
        }
        catch
        {
            // Нет прав — пропускаем
        }
    }

    /// <summary>
    /// Проверить HMAC-подпись TrialInfo.
    /// </summary>
    public static bool VerifySignature(TrialInfo trial)
    {
        if (string.IsNullOrEmpty(trial.Signature))
            return false;

        var canonical = BuildCanonical(trial);
        var expected = ComputeHmac(canonical);

        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(expected),
            Encoding.UTF8.GetBytes(trial.Signature));
    }

    // ----- приватные -----

    private static TrialInfo? TryLoadFromFile(string filePath)
    {
        try
        {
            if (!File.Exists(filePath))
                return null;

            var json = File.ReadAllText(filePath);
            var trial = JsonSerializer.Deserialize<TrialInfo>(json, JsonOptions);
            if (trial is null)
                return null;

            if (!VerifySignature(trial))
                return null;

            return trial;
        }
        catch
        {
            return null;
        }
    }

    private static TrialInfo? TryLoadFromRegistry()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(RegistryPath, writable: false);
            var raw = key?.GetValue(RegistryValueName) as string;
            if (string.IsNullOrEmpty(raw))
                return null;

            var trial = JsonSerializer.Deserialize<TrialInfo>(raw, JsonOptions);
            if (trial is null)
                return null;

            if (!VerifySignature(trial))
                return null;

            return trial;
        }
        catch
        {
            return null;
        }
    }

    private static string BuildCanonical(TrialInfo trial)
    {
        // Копия без поля signature — тот же алгоритм при подписи и проверке
        var copy = new TrialInfo
        {
            FirstRunAt = trial.FirstRunAt,
            LastRunAt = trial.LastRunAt,
            TrialDays = trial.TrialDays,
            Signature = string.Empty,
        };

        return JsonSerializer.Serialize(copy, JsonOptions);
    }

    private static string ComputeHmac(string data)
    {
        using var hmac = new HMACSHA256(HmacSecret);
        var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(data));
        return Convert.ToBase64String(hash);
    }
}