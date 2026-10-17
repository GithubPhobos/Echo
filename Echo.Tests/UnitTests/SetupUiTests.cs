using Echo.UI;
using Spectre.Console.Testing;

namespace Echo.Tests.UnitTests;

public sealed class SetupUiTests
{
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