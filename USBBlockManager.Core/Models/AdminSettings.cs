namespace USBBlockManager.Models;

public class AdminSettings
{
    /// <summary>Блокировать USB по умолчанию, если устройство не в белом списке.</summary>
    public bool BlockByDefault { get; set; } = true;

    /// <summary>Таймаут сессии администратора в минутах.</summary>
    public int SessionTimeoutMinutes { get; set; } = 10;

    /// <summary>Разрешать только чтение для устройств вне белого списка.</summary>
    public bool ReadOnlyForUnlisted { get; set; } = true;

    /// <summary>Логировать все подключения USB.</summary>
    public bool LogAllConnections { get; set; } = true;
}