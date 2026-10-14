using USBBlockManager.Models;
using USBBlockManager.Services;

namespace USBBlockManager.Tests;

public class UsbBlockerTests
{
    private const string UsbStor = UsbBlocker.UsbStorKey;
    private const string Start = UsbBlocker.StartValueName;
    private const string MarkerKey = UsbBlocker.BlockMarkerKey;
    private const string MarkerValue = UsbBlocker.BlockMarkerValue;
    private const string OriginalStart = UsbBlocker.OriginalStartValue;

    [Fact]
    public void IsBlocked_ShouldBeFalse_OnFreshRegistry()
    {
        var reg = new FakeRegistryService();
        var blocker = new UsbBlocker(reg);

        blocker.IsBlocked().Should().BeFalse();
    }

    [Fact]
    public void Block_ShouldSetStartTo4AndMarker()
    {
        var reg = new FakeRegistryService();
        reg.WriteDword(UsbStor, Start, 3);
        var blocker = new UsbBlocker(reg);

        blocker.Block().Should().BeTrue();

        reg.ReadDword(UsbStor, Start).Should().Be(4);
        reg.ReadDword(MarkerKey, MarkerValue).Should().Be(1);
        reg.ReadDword(MarkerKey, OriginalStart).Should().Be(3);
        blocker.IsBlocked().Should().BeTrue();
    }

    [Fact]
    public void Block_ShouldPreserveOriginalStart_FromRegistry()
    {
        var reg = new FakeRegistryService();
        reg.WriteDword(UsbStor, Start, 3);
        var blocker = new UsbBlocker(reg);

        blocker.Block();
        blocker.Unblock();

        reg.ReadDword(UsbStor, Start).Should().Be(3);
    }

    [Fact]
    public void Block_ShouldUseDefault3_WhenStartNotSet()
    {
        var reg = new FakeRegistryService();
        var blocker = new UsbBlocker(reg);

        blocker.Block();

        reg.ReadDword(MarkerKey, OriginalStart).Should().Be(3);
        reg.ReadDword(UsbStor, Start).Should().Be(4);
    }

    [Fact]
    public void Block_ShouldBeIdempotent()
    {
        var reg = new FakeRegistryService();
        var blocker = new UsbBlocker(reg);

        blocker.Block().Should().BeTrue();
        blocker.Block().Should().BeFalse(); // уже заблокировано
    }

    [Fact]
    public void Unblock_ShouldRestoreOriginalValue()
    {
        var reg = new FakeRegistryService();
        reg.WriteDword(UsbStor, Start, 3);
        var blocker = new UsbBlocker(reg);

        blocker.Block();
        blocker.Unblock().Should().BeTrue();

        reg.ReadDword(UsbStor, Start).Should().Be(3);
        reg.ReadDword(MarkerKey, MarkerValue).Should().BeNull();
        reg.ReadDword(MarkerKey, OriginalStart).Should().BeNull();
        blocker.IsBlocked().Should().BeFalse();
    }

    [Fact]
    public void Unblock_ShouldReturnFalse_WhenNotBlocked()
    {
        var reg = new FakeRegistryService();
        var blocker = new UsbBlocker(reg);

        blocker.Unblock().Should().BeFalse();
    }

    [Fact]
    public void Unblock_ShouldNotRestoreTo4_IfOriginalWas4()
    {
        // Защита: если кто-то случайно сохранил 4 как «оригинал»,
        // мы не должны оставить USB заблокированным навсегда.
        var reg = new FakeRegistryService();
        reg.WriteDword(MarkerKey, MarkerValue, 1);
        reg.WriteDword(MarkerKey, OriginalStart, 4);
        reg.WriteDword(UsbStor, Start, 4);
        var blocker = new UsbBlocker(reg);

        blocker.Unblock();

        reg.ReadDword(UsbStor, Start).Should().Be(3);
    }

    [Fact]
    public void IsBlocked_ShouldBeFalse_WhenStartIsNot4()
    {
        var reg = new FakeRegistryService();
        reg.WriteDword(MarkerKey, MarkerValue, 1);
        reg.WriteDword(UsbStor, Start, 3);
        var blocker = new UsbBlocker(reg);

        blocker.IsBlocked().Should().BeFalse();
    }

    [Fact]
    public void IsBlocked_ShouldBeFalse_WhenMarkerMissing()
    {
        // Кто-то другой (не мы) выставил Start=4 — не наша блокировка
        var reg = new FakeRegistryService();
        reg.WriteDword(UsbStor, Start, 4);
        var blocker = new UsbBlocker(reg);

        blocker.IsBlocked().Should().BeFalse();
    }

    [Fact]
    public void GetUnlistedDevices_ShouldReturnOnlyNotInWhitelist()
    {
        var tempFile = Path.Combine(Path.GetTempPath(),
            $"usbblock-wl-{Guid.NewGuid():N}.json");
        try
        {
            var wl = new WhitelistManager(tempFile);
            wl.Add(new WhitelistEntry { Vid = "A", Pid = "B", Serial = "S1" });

            var devices = new List<UsbDevice>
            {
                new() { Vid = "A", Pid = "B", Serial = "S1" }, // в белом списке
                new() { Vid = "X", Pid = "Y", Serial = "S2" }, // не в белом списке
                new() { Vid = "Z", Pid = "W", Serial = "S3" }, // не в белом списке
            };

            var reg = new FakeRegistryService();
            var blocker = new UsbBlocker(reg);

            var unlisted = blocker.GetUnlistedDevices(devices, wl);

            unlisted.Should().HaveCount(2);
            unlisted.Should().Contain(d => d.Serial == "S2");
            unlisted.Should().Contain(d => d.Serial == "S3");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }
}