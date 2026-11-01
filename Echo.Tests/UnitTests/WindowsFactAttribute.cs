namespace Echo.Tests.UnitTests;

/// <summary>
///     A test of Windows code that runs only on Windows; anywhere else it is counted as skipped, not as passed.
/// </summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows only.";
    }
}