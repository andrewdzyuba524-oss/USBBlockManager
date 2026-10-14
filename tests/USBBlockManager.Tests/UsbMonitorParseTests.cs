using USBBlockManager.Services;

namespace USBBlockManager.Tests;

public class UsbMonitorParseTests
{
    [Fact]
    public void ParsePnpDeviceId_ShouldExtractVidPidSerial()
    {
        var pnp = @"USBSTOR\DISK&VEN_KINGSTON&PROD_DATATRAVELER&REV_1.00\AA04012700012234&0";

        var (vid, pid, serial) = UsbMonitor.ParsePnpDeviceId(pnp);

        vid.Should().Be("KINGSTON");
        pid.Should().Be("DATATRAVELER");
        serial.Should().Be("AA04012700012234");
    }

    [Fact]
    public void ParsePnpDeviceId_ShouldHandleOtherVendors()
    {
        var pnp = @"USBSTOR\DISK&VEN_SANDISK&PROD_CRUZER_BLADE&REV_1.00\4C530001120512116283&0";

        var (vid, pid, serial) = UsbMonitor.ParsePnpDeviceId(pnp);

        vid.Should().Be("SANDISK");
        pid.Should().Be("CRUZER_BLADE");
        serial.Should().Be("4C530001120512116283");
    }

    [Fact]
    public void ParsePnpDeviceId_ShouldReturnEmpty_ForGarbage()
    {
        var (vid, pid, serial) = UsbMonitor.ParsePnpDeviceId("not a real pnp id");

        vid.Should().BeEmpty();
        pid.Should().BeEmpty();
        serial.Should().BeEmpty();
    }
}