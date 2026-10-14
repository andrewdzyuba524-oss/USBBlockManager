using System.Management;

namespace USBBlockManager.Services;

/// <summary>
/// Отслеживание подключения USB-накопителей через WMI.
/// Использует Win32_DiskDrive — событие срабатывает раньше,
/// чем диск получит букву и появится в проводнике.
/// </summary>
public class UsbEventWatcher : IDisposable
{
    private ManagementEventWatcher? _insertWatcher;
    private ManagementEventWatcher? _removeWatcher;

    public event EventHandler<UsbEventArgs>? DeviceInserted;
    public event EventHandler<UsbEventArgs>? DeviceRemoved;

    public void Start()
    {

        var insertQuery = new WqlEventQuery(
            "SELECT * FROM __InstanceCreationEvent " +
            "WITHIN 1 " +
            "WHERE TargetInstance ISA 'Win32_DiskDrive' " +
            "AND TargetInstance.InterfaceType = 'USB'");

        _insertWatcher = new ManagementEventWatcher(insertQuery);
        _insertWatcher.EventArrived += OnInserted;
        _insertWatcher.Start();

        var removeQuery = new WqlEventQuery(
            "SELECT * FROM __InstanceDeletionEvent " +
            "WITHIN 1 " +
            "WHERE TargetInstance ISA 'Win32_DiskDrive' " +
            "AND TargetInstance.InterfaceType = 'USB'");

        _removeWatcher = new ManagementEventWatcher(removeQuery);
        _removeWatcher.EventArrived += OnRemoved;
        _removeWatcher.Start();
    }

    public void Stop()
    {
        _insertWatcher?.Stop();
        _removeWatcher?.Stop();
    }

    private void OnInserted(object sender, EventArrivedEventArgs e)
    {
        var pnpId = ExtractPnpDeviceId(e);
        if (pnpId is not null)
            DeviceInserted?.Invoke(this, new UsbEventArgs(pnpId));
    }

    private void OnRemoved(object sender, EventArrivedEventArgs e)
    {
        var pnpId = ExtractPnpDeviceId(e);
        if (pnpId is not null)
            DeviceRemoved?.Invoke(this, new UsbEventArgs(pnpId));
    }

    private static string? ExtractPnpDeviceId(EventArrivedEventArgs e)
    {
        try
        {
            var target = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            return target["PNPDeviceID"]?.ToString(); // "USBSTOR\DISK&VEN_..."
        }
        catch
        {
            return null;
        }
    }

    public void Dispose()
    {
        Stop();
        _insertWatcher?.Dispose();
        _removeWatcher?.Dispose();
    }
}

public class UsbEventArgs : EventArgs
{
    /// <summary>PNPDeviceID устройства, например "USBSTOR\DISK&VEN_...\SERIAL&0".</summary>
    public string PnpDeviceId { get; }

    public UsbEventArgs(string value) => PnpDeviceId = value;
}