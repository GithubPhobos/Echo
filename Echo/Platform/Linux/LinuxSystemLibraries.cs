namespace Echo.Platform.Linux;

/// <summary>
///     The Linux build of Whisper needs the C++ library and the OpenMP library of the system ('libstdc++6' and
///     'libgomp1' on Ubuntu and Debian). The first is everywhere; the second is missing from minimal systems, WSL
///     among them.
/// </summary>
internal sealed class LinuxSystemLibraries : ISystemLibraries
{
    /// <summary>
    ///     The OpenMP library, by the name the recognition library asks for it.
    /// </summary>
    public const string OpenMp = "libgomp.so.1";

    private const string CppLibrary = "libstdc++.so.6";

    /// <inheritdoc/>
    public IReadOnlyList<string> FindMissing() =>
        new[] { CppLibrary, OpenMp }.Where(library => !IsInSystem(library)).ToList();

    private static bool IsInSystem(string library)
    {
        if (!NativeLibrary.TryLoad(library, out IntPtr handle))
            return false;

        NativeLibrary.Free(handle);

        return true;
    }
}