using System.Text.Json;
using System.Text.Json.Serialization;
using USBBlockManager.Models;

namespace USBBlockManager.Services;

public class WhitelistManager
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private readonly string _filePath;
    private readonly object _lock = new();

    public WhitelistManager(string filePath)
    {
        _filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
    }

    public string FilePath => _filePath;

    public List<WhitelistEntry> LoadAll()
    {
        lock (_lock)
        {
            if (!File.Exists(_filePath))
                return new List<WhitelistEntry>();

            try
            {
                var json = File.ReadAllText(_filePath);
                if (string.IsNullOrWhiteSpace(json))
                    return new List<WhitelistEntry>();

                var list = JsonSerializer.Deserialize<List<WhitelistEntry>>(json, JsonOptions);
                return list ?? new List<WhitelistEntry>();
            }
            catch (JsonException)
            {
                // Битый файл — не падаем, возвращаем пустой список
                return new List<WhitelistEntry>();
            }
            catch (IOException)
            {
                // Файл заблокирован или недоступен — тоже не падаем
                return new List<WhitelistEntry>();
            }
        }
    }

    /// <summary>
    /// Атомарная запись: сначала во временный файл, потом переименование.
    /// Гарантирует, что при сбое старый whitelist не будет потерян.
    /// </summary>
    public void SaveAll(IEnumerable<WhitelistEntry> entries)
    {
        if (entries is null) throw new ArgumentNullException(nameof(entries));

        lock (_lock)
        {
            var dir = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            var list = entries.ToList();
            var json = JsonSerializer.Serialize(list, JsonOptions);

            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, json);

            // File.Move с overwrite: true атомарно заменяет файл на NTFS
            File.Move(tempPath, _filePath, overwrite: true);
        }
    }

    public bool Contains(UsbDevice device)
    {
        if (device is null) throw new ArgumentNullException(nameof(device));
        return Contains(device.IdentityKey);
    }

    public bool Contains(string identityKey)
    {
        if (string.IsNullOrWhiteSpace(identityKey))
            return false;

        var all = LoadAll();
        return all.Any(e => string.Equals(e.IdentityKey, identityKey, StringComparison.OrdinalIgnoreCase));
    }

    public WhitelistEntry? FindByInstanceId(string deviceInstanceId)
    {
        if (string.IsNullOrWhiteSpace(deviceInstanceId))
            return null;

        var all = LoadAll();
        return all.FirstOrDefault(e =>
            !string.IsNullOrEmpty(e.Vid)
            && !string.IsNullOrEmpty(e.Pid)
            && !string.IsNullOrEmpty(e.Serial)
            && deviceInstanceId.Contains(e.Vid, StringComparison.OrdinalIgnoreCase)
            && deviceInstanceId.Contains(e.Pid, StringComparison.OrdinalIgnoreCase)
            && deviceInstanceId.Contains(e.Serial, StringComparison.OrdinalIgnoreCase));
    }

    public bool Add(WhitelistEntry entry)
    {
        if (entry is null) throw new ArgumentNullException(nameof(entry));

        lock (_lock)
        {
            var all = LoadAll();
            if (all.Any(e => string.Equals(e.IdentityKey, entry.IdentityKey, StringComparison.OrdinalIgnoreCase)))
                return false;

            all.Add(entry);
            SaveAll(all);
            return true;
        }
    }

    public bool Remove(string identityKey)
    {
        if (string.IsNullOrWhiteSpace(identityKey))
            return false;

        lock (_lock)
        {
            var all = LoadAll();
            int removed = all.RemoveAll(e =>
                string.Equals(e.IdentityKey, identityKey, StringComparison.OrdinalIgnoreCase));

            if (removed == 0)
                return false;

            SaveAll(all);
            return true;
        }
    }

    public void Clear()
    {
        lock (_lock)
        {
            SaveAll(Array.Empty<WhitelistEntry>());
        }
    }

    public int Count => LoadAll().Count;
}