using USBBlockManager.Models;
using USBBlockManager.Services;

namespace USBBlockManager.Tests;

public class WhitelistManagerTests : IDisposable
{
    private readonly string _tempFile;

    public WhitelistManagerTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(),
            $"usbblock-whitelist-{Guid.NewGuid():N}.json");
    }

    [Fact]
    public void LoadAll_ShouldReturnEmpty_WhenFileDoesNotExist()
    {
        var mgr = new WhitelistManager(_tempFile);

        mgr.LoadAll().Should().BeEmpty();
        mgr.Count.Should().Be(0);
    }

    [Fact]
    public void Add_ShouldPersistEntry()
    {
        var mgr = new WhitelistManager(_tempFile);
        var device = new UsbDevice
        {
            Vid = "0951",
            Pid = "1666",
            Serial = "SN1",
            Label = "Kingston",
        };
        var entry = WhitelistEntry.FromDevice(device, "admin");

        mgr.Add(entry).Should().BeTrue();

        var all = mgr.LoadAll();
        all.Should().HaveCount(1);
        all[0].IdentityKey.Should().Be("0951:1666:SN1");
        all[0].Label.Should().Be("Kingston");
        all[0].AddedBy.Should().Be("admin");
    }

    [Fact]
    public void Add_ShouldNotDuplicateSameDevice()
    {
        var mgr = new WhitelistManager(_tempFile);
        var entry = new WhitelistEntry
        {
            Vid = "0951",
            Pid = "1666",
            Serial = "SN1",
            Label = "Kingston",
        };

        mgr.Add(entry).Should().BeTrue();
        mgr.Add(entry).Should().BeFalse();

        mgr.Count.Should().Be(1);
    }

    [Fact]
    public void Contains_ShouldMatchByIdentityKeyCaseInsensitive()
    {
        var mgr = new WhitelistManager(_tempFile);
        mgr.Add(new WhitelistEntry
        {
            Vid = "0951",
            Pid = "1666",
            Serial = "AbCd",
            Label = "Kingston",
        });

        mgr.Contains("0951:1666:abcd").Should().BeTrue();
        mgr.Contains("0951:1666:ABCD").Should().BeTrue();
        mgr.Contains("0951:1666:other").Should().BeFalse();
    }

    [Fact]
    public void Contains_ByUsbDevice_ShouldWork()
    {
        var mgr = new WhitelistManager(_tempFile);
        var device = new UsbDevice { Vid = "0951", Pid = "1666", Serial = "SN1" };

        mgr.Contains(device).Should().BeFalse();
        mgr.Add(WhitelistEntry.FromDevice(device, "admin"));
        mgr.Contains(device).Should().BeTrue();
    }

    [Fact]
    public void Remove_ShouldDeleteEntry()
    {
        var mgr = new WhitelistManager(_tempFile);
        mgr.Add(new WhitelistEntry { Vid = "A", Pid = "B", Serial = "C" });

        mgr.Remove("A:B:C").Should().BeTrue();
        mgr.Remove("A:B:C").Should().BeFalse();
        mgr.Count.Should().Be(0);
    }

    [Fact]
    public void Clear_ShouldRemoveEverything()
    {
        var mgr = new WhitelistManager(_tempFile);
        mgr.Add(new WhitelistEntry { Vid = "A", Pid = "B", Serial = "C" });
        mgr.Add(new WhitelistEntry { Vid = "D", Pid = "E", Serial = "F" });

        mgr.Clear();

        mgr.Count.Should().Be(0);
    }

    [Fact]
    public void LoadAll_ShouldReturnEmpty_WhenFileIsCorrupted()
    {
        File.WriteAllText(_tempFile, "{ broken json ]");
        var mgr = new WhitelistManager(_tempFile);

        mgr.LoadAll().Should().BeEmpty();
    }

    [Fact]
    public void SaveAndLoad_ShouldRoundtrip()
    {
        var mgr1 = new WhitelistManager(_tempFile);
        mgr1.Add(new WhitelistEntry
        {
            Vid = "0951",
            Pid = "1666",
            Serial = "SN1",
            Label = "Kingston",
            AddedBy = "admin",
            Comment = "рабочая",
        });

        var mgr2 = new WhitelistManager(_tempFile);
        var all = mgr2.LoadAll();

        all.Should().HaveCount(1);
        all[0].Comment.Should().Be("рабочая");
        all[0].IdentityKey.Should().Be("0951:1666:SN1");
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }
}