namespace Echo.Services.Contracts;

/// <summary>
///     The dictation history: every recognized phrase kept on this computer, a line a phrase in a file a month,
///     for looking back at what was said and how it was recognized. Nothing of it is sent anywhere.
/// </summary>
internal interface IDictationMemory
{
    /// <summary>
    ///     Keeps a recognized phrase, unless the user turned the history off. An ordinary write failure is only
    ///     noted in the log; the caller can still show and insert the recognized text.
    /// </summary>
    /// <param name="text">What was recognized.</param>
    /// <param name="held">How long the key was held.</param>
    /// <param name="audio">How much sound went to recognition, once the silence before the voice was cut off.</param>
    /// <param name="recognition">How long the recognition took.</param>
    void Add(string text, TimeSpan held, TimeSpan audio, TimeSpan recognition);
}