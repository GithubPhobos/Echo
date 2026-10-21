using System.Globalization;
using System.Runtime.Versioning;
using Echo.Platform.Linux;
using Echo.Setup.Hotkeys;

namespace Echo.Tests.UnitTests;

[SupportedOSPlatform("linux")]
[Collection(CultureCollection.Name)]
public sealed class PortalDesktopAccessTests : IDisposable
{
    private readonly CultureInfo _originalCulture = CultureInfo.CurrentUICulture;

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _originalCulture;
    }

    [LinuxFact(FakeDesktop.BusProgram)]
    public async Task AssignKeyAsync_RussianInterface_DesktopShowsWhatTheKeyDoesInRussian()
    {
        // Arrange
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("ru");

        await using FakeDesktop desktop = await FakeDesktop.StartAsync();
        using var portal = new DesktopPortal(new MemorySettings(), desktop.Address);

        // Act
        await new PortalDesktopAccess(portal).AssignKeyAsync(HotkeyChoice.Default, CancellationToken.None);

        // Assert
        Assert.Equal("Echo: держите и говорите", desktop.Description);
    }
}