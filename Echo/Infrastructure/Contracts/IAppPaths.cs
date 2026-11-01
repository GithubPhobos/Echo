namespace Echo.Infrastructure.Contracts;

/// <summary>
///     Locations of everything the application writes. Nothing is ever written next to the executable,
///     so the files survive an update of the application.
/// </summary>
internal interface IAppPaths
{
    /// <summary>
    ///     The file with the choices made in the setup wizard.
    /// </summary>
    string SettingsFile { get; }

    /// <summary>
    ///     Where Whisper model files live.
    /// </summary>
    string ModelsDirectory { get; }

    /// <summary>
    ///     Where the NVIDIA libraries required by the CUDA backend live.
    /// </summary>
    string CudaDirectory { get; }

    /// <summary>
    ///     Where the log is written, one file a day.
    /// </summary>
    string LogsDirectory { get; }

    /// <summary>
    ///     Where the dictation history is kept, one file a month.
    /// </summary>
    string MemoryDirectory { get; }
}