using System.Globalization;
using Echo.Setup.Downloads;
using Echo.UI;
using Spectre.Console;
using Spectre.Console.Testing;

namespace Echo.Tests.UnitTests;

[Collection(CultureCollection.Name)]
public sealed class SetupUiTests : IDisposable
{
    private const long Megabyte = 1024 * 1024;

    private readonly CultureInfo _originalCulture = CultureInfo.CurrentUICulture;

    public SetupUiTests()
    {
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo("en");
    }

    public void Dispose()
    {
        CultureInfo.CurrentUICulture = _originalCulture;
    }

    [Fact]
    public void BuildCopy_FileTheUserHas_IsShownAsStepsWithNoSpeedOfADownload()
    {
        // Arrange
        var ui = new SetupUi(new TestConsole(), Theme.Full, TimeSpan.FromMilliseconds(1));
        var progress = new DownloadProgress(196 * Megabyte, 465 * Megabyte, 310 * Megabyte, IsVerifying: false);

        // Act
        string[] lines = [.. ui.BuildCopy("ggml-small.bin", 465 * Megabyte, progress, TimeSpan.Zero).Select(Markup.Remove)];

        // Assert: found on the disk, copying into Echo's folder, done; nothing says how fast, as a download would
        Assert.Equal(
            [
                "√ Found on your disk: ggml-small.bin  465 MB",
                "⠋ Copying into Echo's folder   196 MB of 465 MB  42%",
                "○ Done",
            ],
            lines);
    }

    [Fact]
    public void BuildCopy_TerminalWithoutUnicode_MarksTheStepsWithPlainCharacters()
    {
        // Arrange
        var ui = new SetupUi(new TestConsole(), Theme.Ascii, TimeSpan.FromMilliseconds(1));

        // Act
        string[] marks = [.. ui.BuildCopy("libcublas.so.13", 100 * Megabyte, default, TimeSpan.Zero).Select(line => Markup.Remove(line)[..1])];

        // Assert
        Assert.Equal(["+", "*", "-"], marks);
    }

    [Fact]
    public void Meter_GrowsUpwardsThroughItsRows()
    {
        // Arrange: silence, a quiet sound, and one as loud as speech gets
        var ui = new SetupUi(new TestConsole(), Theme.Full, TimeSpan.FromMilliseconds(1));

        // Act
        IReadOnlyList<string> rows = ui.Meter([0f, 1f / 12, 1f / 3], rows: 3);

        // Assert: from the top row down; silence still leaves a line at the bottom
        Assert.Equal(
            [
                $"[{Theme.Highlight}]  █[/]",
                $"[{Theme.Highlight}] ▅█[/]",
                $"[{Theme.Accent}]▁██[/]",
            ],
            rows);
    }
}