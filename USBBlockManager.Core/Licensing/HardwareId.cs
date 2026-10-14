using System.Management;
using System.Security.Cryptography;
using System.Text;

namespace USBBlockManager.Licensing;

/// <summary>
/// Генерация Hardware ID компьютера.
///
/// Собирает:
///   • CPU ID (Win32_Processor.ProcessorId)
///   • Серийный номер материнской платы (Win32_BaseBoard.SerialNumber)
///   • Все MAC-адреса физических сетевых адаптеров (отсортированные)
///
/// Затем хеширует SHA256 и возвращает первые 16 байт в виде
/// "AB12-CD34-EF56-7890-GH12-IJ34-KL56-MN78".
///
/// Устойчив к:
///   • Изменению порядка сетевых карт (сортировка MAC).
///   • Изменению количества сетевых карт (не влияет: используется
///     конкатенация всех MAC в стабильном порядке).
///   • Недоступности отдельных источников (fallback).
///
/// Не устойчив к:
///   • Замене материнской платы или CPU (Hardware ID изменится).
///     В этом случае лицензию нужно перевыпустить.
/// </summary>
public static class HardwareId
{
    private static string? _cached;
    private static readonly object _lock = new();

    /// <summary>
    /// Вычислить Hardware ID. Результат кэшируется в рамках процесса —
    /// повторные вызовы возвращают то же значение, даже если WMI
    /// между вызовами отдаёт разные данные.
    /// </summary>
    public static string Get()
    {
        lock (_lock)
        {
            if (_cached is not null)
                return _cached;

            var parts = new List<string>();

            // 1. CPU ID — самый стабильный источник
            var cpu = TryQueryFirst(
                "SELECT ProcessorId FROM Win32_Processor",
                "ProcessorId");
            if (!string.IsNullOrWhiteSpace(cpu))
                parts.Add("CPU:" + cpu.Trim());

            // 2. Материнская плата — тоже стабильный источник
            var board = TryQueryFirst(
                "SELECT SerialNumber FROM Win32_BaseBoard",
                "SerialNumber");
            if (!string.IsNullOrWhiteSpace(board))
                parts.Add("BB:" + board.Trim());

            // 3. MAC-адреса физических адаптеров — собираем ВСЕ и сортируем.
            //    Это защищает от изменения порядка перечисления Windows.
            var macs = TryQueryAll(
                "SELECT MACAddress FROM Win32_NetworkAdapter " +
                "WHERE PhysicalAdapter = TRUE AND MACAddress IS NOT NULL",
                "MACAddress");
            if (macs.Count > 0)
            {
                var normalized = macs
                    .Select(m => m.Replace(":", "").Replace("-", "").ToUpperInvariant().Trim())
                    .Where(m => m.Length > 0)
                    .Distinct()
                    .OrderBy(m => m, StringComparer.Ordinal)
                    .ToList();

                if (normalized.Count > 0)
                    parts.Add("MAC:" + string.Join(",", normalized));
            }

            if (parts.Count == 0)
            {
                throw new InvalidOperationException(
                    "Не удалось получить ни одного источника Hardware ID. " +
                    "Проверьте доступ к WMI и наличие сетевых адаптеров.");
            }

            var raw = string.Join("|", parts);
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));

            // Первые 16 байт → 32 hex-символа, разбиваем по 4 через дефис
            var hex = Convert.ToHexString(hash, 0, 16);
            _cached = Format(hex);
            return _cached;
        }
    }

    /// <summary>
    /// Возвращает разбивку по источникам — для отладки и логирования.
    /// </summary>
    public static HardwareIdDetails GetDetailed()
    {
        var cpu = TryQueryFirst(
            "SELECT ProcessorId FROM Win32_Processor",
            "ProcessorId") ?? string.Empty;

        var board = TryQueryFirst(
            "SELECT SerialNumber FROM Win32_BaseBoard",
            "SerialNumber") ?? string.Empty;

        var macs = TryQueryAll(
            "SELECT MACAddress FROM Win32_NetworkAdapter " +
            "WHERE PhysicalAdapter = TRUE AND MACAddress IS NOT NULL",
            "MACAddress")
            .Select(m => m.Replace(":", "").Replace("-", "").ToUpperInvariant().Trim())
            .Where(m => m.Length > 0)
            .Distinct()
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToList();

        return new HardwareIdDetails
        {
            CpuId = cpu.Trim(),
            BoardSerial = board.Trim(),
            MacAddresses = macs,
            ComputedId = Get(),
        };
    }

    /// <summary>Сбросить кэш — только для тестов.</summary>
    internal static void ResetCache()
    {
        lock (_lock)
        {
            _cached = null;
        }
    }

    // ----- приватные -----

    private static string Format(string hex)
    {
        var sb = new StringBuilder(hex.Length + hex.Length / 4);
        for (int i = 0; i < hex.Length; i++)
        {
            if (i > 0 && i % 4 == 0)
                sb.Append('-');
            sb.Append(hex[i]);
        }
        return sb.ToString();
    }

    /// <summary>Первый непустой результат по WQL-запросу.</summary>
    private static string? TryQueryFirst(string wql, string property)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(wql);
            foreach (var obj in searcher.Get())
            {
                var value = obj[property]?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }
        }
        catch
        {
            // Источник недоступен — пропускаем
        }
        return null;
    }

    /// <summary>Все непустые результаты по WQL-запросу.</summary>
    private static List<string> TryQueryAll(string wql, string property)
    {
        var result = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher(wql);
            foreach (var obj in searcher.Get())
            {
                var value = obj[property]?.ToString();
                if (!string.IsNullOrWhiteSpace(value))
                    result.Add(value);
            }
        }
        catch
        {
            // Источник недоступен — возвращаем пустой список
        }
        return result;
    }
}

/// <summary>
/// Разбивка Hardware ID по источникам — для отладки.
/// </summary>
public class HardwareIdDetails
{
    public string CpuId { get; set; } = string.Empty;
    public string BoardSerial { get; set; } = string.Empty;
    public List<string> MacAddresses { get; set; } = new();
    public string ComputedId { get; set; } = string.Empty;

    public override string ToString()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Hardware ID: {ComputedId}");
        sb.AppendLine($"CPU:         {(string.IsNullOrEmpty(CpuId) ? "(недоступен)" : CpuId)}");
        sb.AppendLine($"Board:       {(string.IsNullOrEmpty(BoardSerial) ? "(недоступен)" : BoardSerial)}");
        sb.AppendLine($"MAC count:   {MacAddresses.Count}");
        foreach (var mac in MacAddresses)
            sb.AppendLine($"  • {mac}");
        return sb.ToString();
    }
}