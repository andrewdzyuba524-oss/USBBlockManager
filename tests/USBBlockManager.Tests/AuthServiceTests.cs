using USBBlockManager.Security;

namespace USBBlockManager.Tests;

public class AuthServiceTests : IDisposable
{
    private readonly string _tempFile;

    public AuthServiceTests()
    {
        _tempFile = Path.Combine(Path.GetTempPath(),
            $"usbblock-auth-{Guid.NewGuid():N}.hash");
    }

    [Fact]
    public void IsPasswordSet_ShouldBeFalseOnFreshFile()
    {
        var svc = new AuthService(_tempFile);
        svc.IsPasswordSet.Should().BeFalse();
    }

    [Fact]
    public void SetInitialPassword_ShouldMakeIsPasswordSetTrue()
    {
        var svc = new AuthService(_tempFile);
        svc.SetInitialPassword("AdminPass1!");
        svc.IsPasswordSet.Should().BeTrue();
        svc.VerifyPassword("AdminPass1!").Should().BeTrue();
        svc.VerifyPassword("wrong").Should().BeFalse();
    }

    [Fact]
    public void SetInitialPassword_ShouldThrowIfAlreadySet()
    {
        var svc = new AuthService(_tempFile);
        svc.SetInitialPassword("AdminPass1!");

        var act = () => svc.SetInitialPassword("OtherPass2!");
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ChangePassword_ShouldRequireCorrectCurrentPassword()
    {
        var svc = new AuthService(_tempFile);
        svc.SetInitialPassword("OldPass1!");

        svc.ChangePassword("WrongPass", "NewPass2!").Should().BeFalse();
        svc.VerifyPassword("OldPass1!").Should().BeTrue();

        svc.ChangePassword("OldPass1!", "NewPass2!").Should().BeTrue();
        svc.VerifyPassword("NewPass2!").Should().BeTrue();
        svc.VerifyPassword("OldPass1!").Should().BeFalse();
    }

    public void Dispose()
    {
        if (File.Exists(_tempFile))
            File.Delete(_tempFile);
    }
}