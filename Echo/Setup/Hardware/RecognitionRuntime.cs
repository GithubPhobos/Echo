using Whisper.net.LibraryLoader;

namespace Echo.Setup.Hardware;

/// <inheritdoc cref="IRecognitionRuntime"/>
internal sealed class RecognitionRuntime : IRecognitionRuntime
{
    /// <inheritdoc/>
    public RuntimeLibrary? Loaded => RuntimeOptions.LoadedLibrary;
}