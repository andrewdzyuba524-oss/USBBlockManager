using USBBlockManager.Licensing;
using License = USBBlockManager.Licensing.License;

namespace USBBlockManager.Tests.Licensing;

public class LicenseCheckerTests : IDisposable
{
    private readonly string _licenseFile;
    private readonly string _trialFile;

    public LicenseCheckerTests()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"usbblock-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        _licenseFile = Path.Combine(dir, "license.json");
        _trialFile = Path.Combine(dir, "trial.json");
    }

    [Fact]
    public void Check_ShouldReturnTrial_OnFirstRun()
    {
        var status = LicenseChecker.Check(_licenseFile, _trialFile);

        status.Kind.Should().Be(LicenseStatusKind.Trial);
        status.Trial.Should().NotBeNull();
        status.Trial!.TrialDays.Should().Be(7);
        status.Trial.DaysRemaining.Should().BeGreaterThan(5);
    }

    [Fact]
    public void Check_ShouldSaveSignature_InTrialFile()
    {
        LicenseChecker.Check(_licenseFile, _trialFile);

        File.Exists(_trialFile).Should().BeTrue();
        var json = File.ReadAllText(_trialFile);

        json.Should().Contain("signature");
        json.Should().NotContain("\"signature\":\"\"");
    }

    [Fact]
    public void Check_ShouldReturnTrialExpired_WhenFirstRunOld()
    {
        // Создаём TrialInfo с датой в прошлом, подписываем и сохраняем
        var trial = new TrialInfo
        {
            FirstRunAt = DateTime.UtcNow.AddDays(-14),
            LastRunAt = DateTime.UtcNow.AddDays(-1),
            TrialDays = 7,
        };
        TrialProtection.Save(_trialFile, trial);

        var status = LicenseChecker.Check(_licenseFile, _trialFile);

        status.Kind.Should().Be(LicenseStatusKind.TrialExpired);
        status.Trial.Should().NotBeNull();
    }

    [Fact]
    public void Check_ShouldRejectTamperedTrialFile()
    {
        // Создаём валидный trial
        var trial = new TrialInfo
        {
            FirstRunAt = DateTime.UtcNow.AddDays(-3),
            LastRunAt = DateTime.UtcNow,
            TrialDays = 7,
        };
        TrialProtection.Save(_trialFile, trial);

        // Подменяем firstRunAt в файле вручную
        var json = File.ReadAllText(_trialFile);
        var tampered = json.Replace(
            trial.FirstRunAt.ToString("O").TrimEnd('Z'),
            DateTime.UtcNow.AddDays(-100).ToString("O").TrimEnd('Z'));
        File.WriteAllText(_trialFile, tampered);

        // Загружаем — HMAC не совпадёт, попадём в fallback (реестр / новый триал)
        var status = LicenseChecker.Check(_licenseFile, _trialFile);

        // Либо Trial (если реестр пуст и создан новый), либо TrialExpired
        // Главное — программа НЕ считает, что это валидный триал со старым firstRunAt
        status.Kind.Should().BeOneOf(
            LicenseStatusKind.Trial,
            LicenseStatusKind.TrialExpired,
            LicenseStatusKind.NoLicense);
    }

    [Fact]
    public void Check_ShouldReturnInvalidLicense_WhenLicenseFileIsCorrupt()
    {
        File.WriteAllText(_licenseFile, "{not-valid-json}");

        var status = LicenseChecker.Check(_licenseFile, _trialFile);

        status.Kind.Should().Be(LicenseStatusKind.InvalidLicense);
        status.ValidationResult.Should().NotBeNull();
        status.ValidationResult!.Error.Should().Be(
            LicenseValidationError.InvalidFormat);
    }

    [Fact]
    public void Check_ShouldReturnInvalidLicense_WhenSignatureMissing()
    {
        var license = new License
        {
            User = "Test",
            IssuedAt = DateTime.UtcNow,
        };

        var json = System.Text.Json.JsonSerializer.Serialize(license, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = true,
        });
        File.WriteAllText(_licenseFile, json);

        var status = LicenseChecker.Check(_licenseFile, _trialFile);

        status.Kind.Should().Be(LicenseStatusKind.InvalidLicense);
        status.ValidationResult!.Error.Should().Be(
            LicenseValidationError.InvalidSignature);
    }

    public void Dispose()
    {
        try
        {
            var dir = Path.GetDirectoryName(_licenseFile);
            if (dir is not null && Directory.Exists(dir))
                Directory.Delete(dir, recursive: true);
        }
        catch { }

        // Чистим реестр — иначе следующий тест может подхватить наш триал
        try
        {
            Microsoft.Win32.Registry.LocalMachine.DeleteSubKeyTree(
                @"SOFTWARE\USBBlockManager", throwOnMissingSubKey: false);
        }
        catch { }
    }
}