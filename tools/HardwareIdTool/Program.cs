using USBBlockManager.Licensing;

namespace HardwareIdTool;

/// <summary>
/// Утилита для клиента. Показывает Hardware ID его компьютера.
/// Клиент запускает её, копирует код и отправляет издателю для получения лицензии.
/// </summary>
public static class Program
{
    public static int Main(string[] args)
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        PrintHeader();

        try
        {
            var details = HardwareId.GetDetailed();

            PrintHwidBox(details.ComputedId);

            Console.WriteLine();
            Console.WriteLine("Что дальше:");
            Console.WriteLine("  1. Скопируйте код выше (Ctrl+C).");
            Console.WriteLine("  2. Отправьте его вашему поставщику USB Block Manager.");
            Console.WriteLine("  3. Получите файл license.json и положите его в папку:");
            Console.WriteLine("     C:\\ProgramData\\USBBlockManager\\");
            Console.WriteLine("  4. Запустите USB Block Manager — он подхватит лицензию.");
            Console.WriteLine();

            if (args.Contains("--details", StringComparer.OrdinalIgnoreCase))
            {
                Console.WriteLine();
                Console.WriteLine("Детали Hardware ID (для отладки):");
                Console.WriteLine(details.ToString());
            }

            if (!Console.IsInputRedirected)
            {
                Console.WriteLine("Нажмите любую клавишу для выхода...");
                Console.ReadKey(intercept: true);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Ошибка при получении Hardware ID: {ex.Message}");
            Console.ResetColor();
            Console.WriteLine();
            Console.WriteLine("Возможные причины:");
            Console.WriteLine("  • У вас нет прав на доступ к WMI.");
            Console.WriteLine("  • На компьютере отключён сервис Windows Management Instrumentation.");
            Console.WriteLine("  • Антивирус блокирует доступ к системной информации.");
            Console.WriteLine();
            Console.WriteLine("Попробуйте запустить от имени администратора.");

            if (!Console.IsInputRedirected)
            {
                Console.WriteLine("Нажмите любую клавишу для выхода...");
                Console.ReadKey(intercept: true);
            }

            return 1;
        }
    }

    private static void PrintHeader()
    {
        Console.WriteLine("╔══════════════════════════════════════════════════════════════╗");
        Console.WriteLine("║         USB Block Manager — Активация лицензии               ║");
        Console.WriteLine("╚══════════════════════════════════════════════════════════════╝");
        Console.WriteLine();
        Console.WriteLine("Эта утилита покажет уникальный код вашего компьютера.");
        Console.WriteLine("Отправьте его поставщику для получения лицензии.");
        Console.WriteLine();
    }

    private static void PrintHwidBox(string hwid)
    {
        const int width = 62;
        var line = new string('═', width);

        Console.WriteLine($"╔{line}╗");
        Console.WriteLine($"║ {"Hardware ID вашего компьютера:".PadRight(width - 2)} ║");
        Console.WriteLine($"║{"".PadRight(width)}║");

        // HWID может быть длинным — центрируем
        var padded = hwid.Length >= width - 4
            ? hwid
            : hwid.PadLeft((width + hwid.Length) / 2).PadRight(width);

        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine($"║{padded}║");
        Console.ResetColor();

        Console.WriteLine($"║{"".PadRight(width)}║");
        Console.WriteLine($"╚{line}╝");
    }
}