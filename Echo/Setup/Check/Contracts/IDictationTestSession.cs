using Whisper.net.LibraryLoader;

namespace Echo.Setup.Check.Contracts;

/// <summary>
///     A running dictation pipeline that reports what it hears instead of typing it.
/// </summary>
internal interface IDictationTestSession : IAsyncDisposable
{
    /// <summary>
    ///     The Whisper runtime that was actually loaded.
    /// </summary>
    RuntimeLibrary? Runtime { get; }

    bool TryRead(out DictationTestEvent testEvent);
}