using System.Security.Cryptography;
using System.Text.Json;
using USBBlockManager.Licensing;
using License = USBBlockManager.Licensing.License;

namespace LicenseGenerator;

/// <summary>
/// Утилита издателя USB Block Manager.
///
/// Команды:
///   keygen  — сгенерировать пару ключей (private.pem + public.pem)
///   issue   — выпустить лицензию для клиента
///   verify  — проверить выпущенную лицензию
///   info    — показать Hardware ID текущего компьютера (для отладки)
/// </summary>
public static class Program
{
    private const string PrivateKeyFile = "private.pem";
    private const string PublicKeyFile = "public.pem";

    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        if (args.Length == 0)
        {
            PrintHelp();
            return 1;
        }

        try
        {
            return args[0].ToLowerInvariant() switch
            {
                "keygen" => Keygen(),
                "issue" => Issue(args),
                "verify" => Verify(args),
                "info" => Info(),
                "help" or "-h" or "--help" => HelpAndExit(),
                _ => UnknownCommand(args[0]),
            };
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Ошибка: {ex.Message}");
            Console.ResetColor();
            return 2;
        }
    }

    // ============================================================
    // keygen — генерация пары ключей
    // ============================================================
    private static int Keygen()
    {
        if (File.Exists(PrivateKeyFile))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Файл {PrivateKeyFile} уже существует.");
            Console.WriteLine("Если вы хотите пересоздать ключи — удалите его вручную.");
            Console.WriteLine("ВНИМАНИЕ: все ранее выпущенные лицензии перестанут работать!");
            Console.ResetColor();
            return 3;
        }

        Console.WriteLine("Генерация пары ключей ECDsa (P-256)...");

        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);

        // Приватный ключ — в формате PKCS#8
        var privateKey = ecdsa.ExportPkcs8PrivateKey();
        File.WriteAllText(PrivateKeyFile, Convert.ToBase64String(privateKey));

        // Публичный ключ — в формате SubjectPublicKeyInfo
        var publicKey = ecdsa.ExportSubjectPublicKeyInfo();
        var publicKeyBase64 = Convert.ToBase64String(publicKey);
        File.WriteAllText(PublicKeyFile, publicKeyBase64);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ Приватный ключ сохранён: {PrivateKeyFile}");
        Console.WriteLine($"✓ Публичный ключ сохранён:  {PublicKeyFile}");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine("⚠ ВАЖНО:");
        Console.WriteLine("  1. Храните private.pem в надёжном месте. НЕ передавайте его никому.");
        Console.WriteLine("  2. Если private.pem потерян — вы не сможете выпускать новые лицензии,");
        Console.WriteLine("     но ранее выпущенные продолжат работать.");
        Console.WriteLine("  3. Публичный ключ нужно вставить в USBBlockManager.Core\\Licensing\\LicenseService.cs,");
        Console.WriteLine("     в константу PublicKeyBase64. После этого пересоберите программу.");
        Console.WriteLine();
        Console.WriteLine("Публичный ключ (скопируйте в LicenseService.PublicKeyBase64):");
        Console.WriteLine();
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(publicKeyBase64);
        Console.ResetColor();

        return 0;
    }

    // ============================================================
    // issue — выпуск лицензии
    // ============================================================
    private static int Issue(string[] args)
    {
        if (!File.Exists(PrivateKeyFile))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Не найден {PrivateKeyFile}. Сначала выполните: keygen");
            Console.ResetColor();
            return 4;
        }

        // Парсинг аргументов
        string? user = null;
        string? email = null;
        string? hardware = null;
        string edition = "standard";
        int? days = null;
        string? comment = null;
        string output = "license.json";

        for (int i = 1; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--user": user = NextArg(args, ref i); break;
                case "--email": email = NextArg(args, ref i); break;
                case "--hardware": hardware = NextArg(args, ref i); break;
                case "--edition": edition = NextArg(args, ref i); break;
                case "--days": days = int.Parse(NextArg(args, ref i)); break;
                case "--comment": comment = NextArg(args, ref i); break;
                case "--out": output = NextArg(args, ref i); break;
            }
        }

        if (string.IsNullOrWhiteSpace(user))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine("Не указан --user. Пример:");
            Console.WriteLine("  LicenseGenerator issue --user \"ООО Ромашка\" --hardware AB12-CD34-... --days 365");
            Console.ResetColor();
            return 5;
        }

        var license = new License
        {
            Version = 1,
            User = user,
            Email = email ?? string.Empty,
            HardwareId = hardware ?? string.Empty,
            IssuedAt = DateTime.UtcNow,
            ExpiresAt = days.HasValue ? DateTime.UtcNow.AddDays(days.Value) : null,
            Edition = edition,
            Comment = comment,
        };

        // Подпись
        using var ecdsa = LoadPrivateKey();
        license.Signature = LicenseService.SignLicense(license, ecdsa);

        // Сохранение
        var json = JsonSerializer.Serialize(license, new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        });

        File.WriteAllText(output, json);

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine($"✓ Лицензия выпущена: {output}");
        Console.ResetColor();
        Console.WriteLine();
        Console.WriteLine($"  Пользователь:  {license.User}");
        if (!string.IsNullOrEmpty(license.Email))
            Console.WriteLine($"  Email:         {license.Email}");
        Console.WriteLine($"  Издание:       {license.Edition}");
        if (!string.IsNullOrEmpty(license.HardwareId))
            Console.WriteLine($"  Hardware ID:   {license.HardwareId}");
        else
            Console.WriteLine($"  Hardware ID:   (не привязана — работает на любом ПК)");
        Console.WriteLine($"  Выпущена:      {license.IssuedAt:yyyy-MM-dd HH:mm} UTC");
        if (license.ExpiresAt.HasValue)
            Console.WriteLine($"  Действует до:  {license.ExpiresAt:yyyy-MM-dd HH:mm} UTC");
        else
            Console.WriteLine($"  Действует:     бессрочно");

        return 0;
    }

    // ============================================================
    // verify — проверка лицензии
    // ============================================================
    private static int Verify(string[] args)
    {
        if (args.Length < 2)
        {
            Console.WriteLine("Использование: LicenseGenerator verify <license.json>");
            return 6;
        }

        var path = args[1];
        if (!File.Exists(path))
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Файл не найден: {path}");
            Console.ResetColor();
            return 7;
        }

        var result = LicenseService.ValidateFromFile(path);

        Console.WriteLine($"Файл: {path}");
        Console.WriteLine();
        Console.WriteLine($"Статус:       {(result.IsValid ? "✓ ДЕЙСТВИТЕЛЬНА" : "✗ НЕДЕЙСТВИТЕЛЬНА")}");
        Console.WriteLine($"Причина:      {result.Error}");
        Console.WriteLine($"Сообщение:    {result.Message}");
        if (result.License is not null)
        {
            Console.WriteLine();
            Console.WriteLine($"Пользователь: {result.License.User}");
            Console.WriteLine($"Издание:      {result.License.Edition}");
            Console.WriteLine($"Выпущена:     {result.License.IssuedAt:yyyy-MM-dd HH:mm} UTC");
            Console.WriteLine($"Истекает:     {(result.License.ExpiresAt?.ToString("yyyy-MM-dd HH:mm") ?? "бессрочно")}");
            Console.WriteLine($"Hardware ID:  {result.License.HardwareId}");
        }
        Console.WriteLine($"Текущий HWID: {result.CurrentHardwareId}");

        Console.ForegroundColor = result.IsValid ? ConsoleColor.Green : ConsoleColor.Red;
        Console.WriteLine();
        Console.WriteLine(result.IsValid ? "✓ OK" : "✗ FAIL");
        Console.ResetColor();

        return result.IsValid ? 0 : 1;
    }

    // ============================================================
    // info — показать HWID текущего компьютера
    // ============================================================
    private static int Info()
    {
        try
        {
            var details = HardwareId.GetDetailed();
            Console.WriteLine(details.ToString());
            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Не удалось получить Hardware ID: {ex.Message}");
            Console.ResetColor();
            return 8;
        }
    }

    // ============================================================
    // Вспомогательное
    // ============================================================
    private static ECDsa LoadPrivateKey()
    {
        var base64 = File.ReadAllText(PrivateKeyFile).Trim();
        var bytes = Convert.FromBase64String(base64);

        var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(bytes, out _);
        return ecdsa;
    }

    private static string NextArg(string[] args, ref int i)
    {
        if (i + 1 >= args.Length)
            throw new ArgumentException($"Не указано значение для {args[i]}");
        return args[++i];
    }

    private static int HelpAndExit()
    {
        PrintHelp();
        return 0;
    }

    private static int UnknownCommand(string cmd)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine($"Неизвестная команда: {cmd}");
        Console.ResetColor();
        PrintHelp();
        return 1;
    }

    private static void PrintHelp()
    {
        Console.WriteLine("LicenseGenerator — утилита издателя USB Block Manager");
        Console.WriteLine();
        Console.WriteLine("Команды:");
        Console.WriteLine("  keygen   — сгенерировать пару ключей (private.pem + public.pem)");
        Console.WriteLine("  issue    — выпустить лицензию");
        Console.WriteLine("  verify   — проверить лицензию");
        Console.WriteLine("  info     — показать Hardware ID текущего компьютера");
        Console.WriteLine();
        Console.WriteLine("Примеры:");
        Console.WriteLine("  LicenseGenerator keygen");
        Console.WriteLine("  LicenseGenerator issue --user \"ООО Ромашка\" --hardware AB12-CD34-... --days 365");
        Console.WriteLine("  LicenseGenerator issue --user \"Иван Иванов\" --days 30 --out trial.json");
        Console.WriteLine("  LicenseGenerator verify license.json");
        Console.WriteLine();
        Console.WriteLine("Параметры issue:");
        Console.WriteLine("  --user       Имя лицензиата (обязательно)");
        Console.WriteLine("  --email      Email (опционально)");
        Console.WriteLine("  --hardware   Hardware ID компьютера клиента (опционально)");
        Console.WriteLine("  --edition    Издание: trial/standard/pro/enterprise (по умолчанию standard)");
        Console.WriteLine("  --days       Срок действия в днях (опционально, без него — бессрочно)");
        Console.WriteLine("  --comment    Комментарий издателя (опционально)");
        Console.WriteLine("  --out        Имя файла лицензии (по умолчанию license.json)");
    }
}