using USBBlockManager.Licensing;

namespace USBBlockManager.Tests.Licensing;

public class HardwareIdTests
{
    [Fact]
    public void Get_ShouldReturnValidFormat()
    {
        var id = HardwareId.Get();

        id.Should().NotBeNullOrWhiteSpace();
        id.Should().MatchRegex(@"^[0-9A-F]{4}(-[0-9A-F]{4})+$");
    }

    [Fact]
    public void Get_ShouldBeDeterministic_AcrossMultipleCalls()
    {
        // В рамках одного процесса — всегда одинаково (кэш + стабильные источники)
        var id1 = HardwareId.Get();
        var id2 = HardwareId.Get();
        var id3 = HardwareId.Get();

        id1.Should().Be(id2);
        id2.Should().Be(id3);
    }

    [Fact]
    public void GetDetailed_ShouldReturnComputedId_EqualToGet()
    {
        var detailed = HardwareId.GetDetailed();

        detailed.ComputedId.Should().Be(HardwareId.Get());
    }

    [Fact]
    public void GetDetailed_ShouldHaveAtLeastOneSource()
    {
        var detailed = HardwareId.GetDetailed();

        var hasAnySource =
            !string.IsNullOrEmpty(detailed.CpuId) ||
            !string.IsNullOrEmpty(detailed.BoardSerial) ||
            detailed.MacAddresses.Count > 0;

        hasAnySource.Should().BeTrue(
            "должен быть доступен хотя бы один источник Hardware ID");
    }

    [Fact]
    public void MacAddresses_ShouldBeSortedAndDistinct()
    {
        var detailed = HardwareId.GetDetailed();

        if (detailed.MacAddresses.Count > 1)
        {
            detailed.MacAddresses
                .Should().BeInAscendingOrder(StringComparer.Ordinal);

            detailed.MacAddresses
                .Should().OnlyHaveUniqueItems();
        }
    }
}