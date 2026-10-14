using USBBlockManager.Services;

namespace USBBlockManager.Tests;

public class FakeRegistryServiceTests
{
    private const string TestKey = @"Services\USBSTOR";
    private const string TestValue = "Start";

    [Fact]
    public void ReadDword_ShouldReturnNull_WhenNotSet()
    {
        var reg = new FakeRegistryService();
        reg.ReadDword(TestKey, TestValue).Should().BeNull();
    }

    [Fact]
    public void WriteDword_ShouldMakeValueReadable()
    {
        var reg = new FakeRegistryService();
        reg.WriteDword(TestKey, TestValue, 4);

        reg.ReadDword(TestKey, TestValue).Should().Be(4);
        reg.ValueExists(TestKey, TestValue).Should().BeTrue();
        reg.KeyExists(TestKey).Should().BeTrue();
    }

    [Fact]
    public void DeleteValue_ShouldRemoveValue()
    {
        var reg = new FakeRegistryService();
        reg.WriteDword(TestKey, TestValue, 4);

        reg.DeleteValue(TestKey, TestValue).Should().BeTrue();
        reg.DeleteValue(TestKey, TestValue).Should().BeFalse();
        reg.ValueExists(TestKey, TestValue).Should().BeFalse();
    }

    [Fact]
    public void KeyExists_ShouldBeFalse_WhenNoValuesWritten()
    {
        var reg = new FakeRegistryService();
        reg.KeyExists(TestKey).Should().BeFalse();
    }
}