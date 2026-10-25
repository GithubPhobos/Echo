using Whisper.net.LibraryLoader;

namespace Echo.Setup.Hardware.Contracts;

/// <summary>
///     The native library recognition runs on in this process. It is loaded once per process, the first time
///     a model is: choosing another backend takes effect only in a fresh start.
/// </summary>
internal interface IRecognitionRuntime
{
    /// <summary>
    ///     The loaded library, or null while no model has been loaded yet.
    /// </summary>
    RuntimeLibrary? Loaded { get; }
}