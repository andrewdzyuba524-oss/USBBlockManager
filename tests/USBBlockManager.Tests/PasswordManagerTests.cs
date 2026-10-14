using USBBlockManager.Security;

namespace USBBlockManager.Tests;

public class PasswordManagerTests
{
    [Fact]
    public void Hash_ShouldProduceStorableString()
    {
        var hash = PasswordManager.Hash("Secret123!");

        hash.Should().StartWith("PBKDF2$");
        hash.Split('$').Should().HaveCount(4);
        PasswordManager.IsValidHashFormat(hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_ShouldAcceptCorrectPassword()
    {
        var hash = PasswordManager.Hash("Secret123!");

        PasswordManager.Verify("Secret123!", hash).Should().BeTrue();
    }

    [Fact]
    public void Verify_ShouldRejectWrongPassword()
    {
        var hash = PasswordManager.Hash("Secret123!");

        PasswordManager.Verify("secret123!", hash).Should().BeFalse();
        PasswordManager.Verify("Secret124!", hash).Should().BeFalse();
        PasswordManager.Verify("", hash).Should().BeFalse();
    }

    [Fact]
    public void Hash_ShouldProduceDifferentResultsForSamePassword()
    {
        var hash1 = PasswordManager.Hash("Secret123!");
        var hash2 = PasswordManager.Hash("Secret123!");

        hash1.Should().NotBe(hash2);
        PasswordManager.Verify("Secret123!", hash1).Should().BeTrue();
        PasswordManager.Verify("Secret123!", hash2).Should().BeTrue();
    }

    [Fact]
    public void Verify_ShouldRejectMalformedHash()
    {
        PasswordManager.Verify("x", "not-a-hash").Should().BeFalse();
        PasswordManager.Verify("x", "PBKDF2$notanint$c2FsdA==$aGFzaA==").Should().BeFalse();
        PasswordManager.Verify("x", "").Should().BeFalse();
    }

    [Fact]
    public void Hash_ShouldRejectEmptyPassword()
    {
        var act = () => PasswordManager.Hash("");
        act.Should().Throw<ArgumentException>();
    }
}