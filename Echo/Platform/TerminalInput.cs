using Echo.Listening;
using Echo.Platform.Linux;
using Echo.Platform.Windows;
using Spectre.Console;

namespace Echo.Platform;

/// <summary>Selects input by operating system and console capability, never by terminal brand.</summary>
internal static class TerminalInput
{
    /// <summary>Opens one reader for the screen, retaining existing input when native access is unavailable.</summary>
    internal static ITerminalInput Open(IAnsiConsoleInput fallback, TerminalFocusReports reports)
    {
        if (OperatingSystem.IsWindows() && WindowsTerminalInput.TryOpen(reports) is { } windows)
            return windows;
        if (OperatingSystem.IsLinux() && LinuxTerminalInput.TryOpen(reports) is { } linux)
            return linux;
        return new ConsoleTerminalInput(fallback);
    }
}