namespace Echo.Services.Contracts;

/// <summary>
///     Acts as the AI engine wrapper, utilizing Whisper.net to perform local, 
///     offline speech-to-text inference on recorded audio streams.
/// </summary>
internal interface IWhisperInferenceService : IDisposable
{
    /// <summary>
    ///     Initialize Whisper model.
    /// </summary>
    void TryInitializeWhisperModel();

    /// <summary>
    ///     Process audio stream.
    /// </summary>
    /// <param name="audioStream">Incopming audio stream as <see cref="MemoryStream"/></param>
    /// <returns>Text recognized from audio stream.</returns>
    Task<string?> ProcessAudioAsync(MemoryStream? audioStream);
}