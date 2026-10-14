using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using USBBlockManager.Licensing;
using License = USBBlockManager.Licensing.License;

namespace USBBlockManager.Tests.Licensing;

public class LicenseServiceTests
{
    [Fact]
    public void ValidateFromJson_ShouldFail_WhenMissingSignature()
    {
        // Ключ настроен, но JSON не содержит подписи — должно быть InvalidSignature
        var json = "{}";
        var result = LicenseService.ValidateFromJson(json);

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(LicenseValidationError.InvalidSignature);
    }

    [Fact]
    public void ValidateFromJson_ShouldFail_WhenEmpty()
    {
        var result = LicenseService.ValidateFromJson("");
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(LicenseValidationError.InvalidFormat);
    }

    [Fact]
    public void ValidateFromJson_ShouldFail_WhenMalformedJson()
    {
        var result = LicenseService.ValidateFromJson("{not-json}");
        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(LicenseValidationError.InvalidFormat);
    }

    [Fact]
    public void ValidateFromFile_ShouldFail_WhenFileNotFound()
    {
        var result = LicenseService.ValidateFromFile(
            Path.Combine(Path.GetTempPath(), "no-such-license.json"));

        result.IsValid.Should().BeFalse();
        result.Error.Should().Be(LicenseValidationError.FileNotFound);
    }

    [Fact]
    public void CanonicalizeForSigning_ShouldRemoveSignature()
    {
        var json = """
        {
          "version": 1,
          "user": "Test",
          "signature": "AAA"
        }
        """;

        var canonical = LicenseService.CanonicalizeForSigning(json);

        canonical.Should().NotContain("signature");
        canonical.Should().NotContain("AAA");
        canonical.Should().Contain("\"user\":\"Test\"");
    }

    [Fact]
    public void CanonicalizeForSigning_ShouldBeDeterministic()
    {
        var json1 = """{"version":1,"user":"Test"}""";
        var json2 = """
        {
          "version": 1,
          "user": "Test"
        }
        """;

        var c1 = LicenseService.CanonicalizeForSigning(json1);
        var c2 = LicenseService.CanonicalizeForSigning(json2);

        c1.Should().Be(c2);
    }

    [Fact]
    public void IsExpired_ShouldBeTrue_WhenPastDate()
    {
        var license = new License
        {
            ExpiresAt = DateTime.UtcNow.AddDays(-1),
        };

        license.IsExpired.Should().BeTrue();
    }

    [Fact]
    public void IsExpired_ShouldBeFalse_WhenNull()
    {
        var license = new License { ExpiresAt = null };
        license.IsExpired.Should().BeFalse();
    }

    [Fact]
    public void DaysUntilExpiry_ShouldBeNull_WhenNoExpiry()
    {
        var license = new License { ExpiresAt = null };
        license.DaysUntilExpiry.Should().BeNull();
    }

    [Fact]
    public void DaysUntilExpiry_ShouldBePositive_WhenFutureDate()
    {
        var license = new License
        {
            ExpiresAt = DateTime.UtcNow.AddDays(30),
        };

        license.DaysUntilExpiry.Should().BeGreaterThan(28);
    }
}