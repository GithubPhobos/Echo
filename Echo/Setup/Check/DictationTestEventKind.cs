namespace Echo.Setup.Check;

/// <summary>
///     What happened during a test dictation.
/// </summary>
internal enum DictationTestEventKind
{
    RecordingStarted,
    Transcribing,
    Recognized,
    NothingHeard,
    Failed
}