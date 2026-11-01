using System.Runtime.Versioning;
using Echo.Listening;
using Echo.Platform.Windows;

namespace Echo.Tests.UnitTests;

[SupportedOSPlatform("windows")]
public sealed class WindowsTerminalInputTests
{
    [Fact]
    public void ReadKey_SiblingPaneLosesFocus_ReportsItAndKeepsTheNextKey()
    {
        var reports = new TerminalFocusReports();
        var records = new Queue<ConsoleInputRecord>([
            new() { Type = 0x10, IsDownOrFocused = 0 },
            new() { Type = 1, IsDownOrFocused = 1, Key = (ushort)ConsoleKey.A, Character = 'a', Repeat = 1 },
        ]);
        using var input = new WindowsTerminalInput(reports, () => records.TryDequeue(out var record) ? record : null);
        Assert.Equal('a', input.ReadKey()!.Value.KeyChar);
        Assert.False(reports.IsFocused);
        records.Enqueue(new ConsoleInputRecord { Type = 0x10, IsDownOrFocused = 1 });
        Assert.Null(input.ReadKey());
        Assert.True(reports.IsFocused);
    }

    [Fact]
    public void ReadKey_RepeatUnicodeAndModifiers_ArePreserved()
    {
        var records = new Queue<ConsoleInputRecord>([
            new() { Type = 1, IsDownOrFocused = 1, Key = (ushort)ConsoleKey.A, Character = 'Ж', Repeat = 3, Modifiers = 0x1a },
            new() { Type = 1, IsDownOrFocused = 0, Key = (ushort)ConsoleKey.A, Character = 'Ж' },
        ]);
        using var input = new WindowsTerminalInput(new TerminalFocusReports(), () => records.TryDequeue(out var record) ? record : null);
        for (int i = 0; i < 3; i++)
        {
            ConsoleKeyInfo key = input.ReadKey()!.Value;
            Assert.Equal('Ж', key.KeyChar);
            Assert.Equal(ConsoleModifiers.Shift | ConsoleModifiers.Control | ConsoleModifiers.Alt, key.Modifiers);
        }
        Assert.Null(input.ReadKey());
    }

    [Fact]
    public void ReadKey_AltNumericTextOnRelease_IsNotLost()
    {
        var records = new Queue<ConsoleInputRecord>([
            new() { Type = 1, IsDownOrFocused = 1, Key = 0x10 },
            new() { Type = 1, IsDownOrFocused = 0, Key = 0x12, Character = 'é', Repeat = 1 },
        ]);
        using var input = new WindowsTerminalInput(new TerminalFocusReports(), () => records.TryDequeue(out var record) ? record : null);
        Assert.Equal('é', input.ReadKey()!.Value.KeyChar);
        Assert.Null(input.ReadKey());
    }
}