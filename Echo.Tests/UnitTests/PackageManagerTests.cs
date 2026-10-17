using Echo.Platform.Linux;

namespace Echo.Tests.UnitTests;

public sealed class PackageManagerTests
{
    [Theory]
    [InlineData("ID=ubuntu", "sudo apt install libpulse0")]
    [InlineData("ID=linuxmint\nID_LIKE=\"ubuntu debian\"", "sudo apt install libpulse0")]
    [InlineData("ID=fedora", "sudo dnf install pulseaudio-libs")]
    [InlineData("ID=manjaro\nID_LIKE=arch", "sudo pacman -S libpulse")]
    [InlineData("ID=\"opensuse-tumbleweed\"\nID_LIKE=\"opensuse suse\"", "sudo zypper install libpulse0")]
    public void FromOsRelease_KnownDistribution_InstallsWithItsOwnPackageName(string osRelease, string expected)
    {
        // Arrange
        var packages = new Dictionary<string, string>
        {
            [PackageManager.Debian] = "libpulse0",
            [PackageManager.Fedora] = "pulseaudio-libs",
            [PackageManager.Arch] = "libpulse",
            [PackageManager.Suse] = "libpulse0",
        };

        // Act
        PackageManager? manager = PackageManager.FromOsRelease(osRelease.Split('\n'));

        // Assert
        Assert.Equal(expected, manager!.Install(packages));
    }

    [Fact]
    public void FromOsRelease_UnknownDistribution_IsNull()
    {
        // Act & Assert
        Assert.Null(PackageManager.FromOsRelease(["ID=gentoo"]));
    }

    [Fact]
    public void Install_PackageWithOneNameEverywhere_UsesIt()
    {
        // Arrange
        PackageManager manager = PackageManager.FromOsRelease(["ID=fedora"])!;

        // Act & Assert
        Assert.Equal("sudo dnf install xsel", manager.Install(new Dictionary<string, string> { [PackageManager.Debian] = "xsel" }));
    }
}