using USBBlockManager.Models;

namespace USBBlockManager.Tests;

public class UsbDeviceTests
{
    [Fact]
    public void IdentityKey_ShouldCombineVidPidSerial()
    {
        var device = new UsbDevice
        {
            Vid = "0951",
            Pid = "1666",
            Serial = "AA04012700012234",
        };

        device.IdentityKey.Should().Be("0951:1666:AA04012700012234");
    }

    [Fact]
    public void WhitelistEntry_FromDevice_CopiesAllFields()
    {
        var device = new UsbDevice
        {
            Vid = "0951",
            Pid = "1666",
            Serial = "SN123",
            Label = "Kingston",
        };

        var entry = WhitelistEntry.FromDevice(device, "admin", "рабочая флешка");

        entry.Vid.Should().Be("0951");
        entry.Pid.Should().Be("1666");
        entry.Serial.Should().Be("SN123");
        entry.Label.Should().Be("Kingston");
        entry.AddedBy.Should().Be("admin");
        entry.Comment.Should().Be("рабочая флешка");
        entry.IdentityKey.Should().Be(device.IdentityKey);
    }
}