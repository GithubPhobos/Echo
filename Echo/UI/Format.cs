namespace Echo.UI;

/// <summary>
///     Formats numbers the same way everywhere in the interface.
/// </summary>
internal static class Format
{
    public static string Megabytes(long bytes)
    {
        double megabytes = bytes / (1024d * 1024);

        return megabytes >= 1000
            ? string.Create(System.Globalization.CultureInfo.CurrentUICulture, $"{megabytes / 1024:0.0} {Strings.UnitGb}")
            : string.Create(System.Globalization.CultureInfo.CurrentUICulture, $"{megabytes:0} {Strings.UnitMb}");
    }

    /// <summary>
    ///     The memory of a graphics card, rounded to whole gigabytes the way it is advertised.
    /// </summary>
    public static string VideoMemory(int megabytes) =>
        string.Create(System.Globalization.CultureInfo.CurrentUICulture, $"{Math.Round(megabytes / 1024d):0} {Strings.UnitGb}");

    public static string Seconds(double milliseconds) =>
        string.Create(System.Globalization.CultureInfo.CurrentUICulture, $"{milliseconds / 1000:0.00}");
}