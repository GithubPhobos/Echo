namespace Echo.Listening.Contracts;

/// <summary>
///     What the dictation services tell the working screen, and what the screen asks of them.
///     Every member may be called from any thread.
/// </summary>
internal interface IListenActivity
{
    ListenState State { get; }

    /// <summary>
    ///     Done once the start-up is through: Echo waits for the key, or could not start.
    /// </summary>
    Task Started { get; }

    /// <summary>
    ///     When the current recording started; meaningful only while recording.
    /// </summary>
    DateTimeOffset RecordingStarted { get; }

    /// <summary>
    ///     Whether the push-to-talk key is ignored for now.
    /// </summary>
    bool IsPaused { get; set; }

    /// <summary>
    ///     The name of the runtime the model runs on, once it is loaded.
    /// </summary>
    string? Runtime { get; set; }

    /// <summary>
    ///     The screen's log, where recognized phrases are added.
    /// </summary>
    ScreenLog Log { get; }

    void SetState(ListenState state);

    /// <summary>
    ///     Records the loudest sample of the latest piece of sound from the microphone, between 0 and 1.
    /// </summary>
    void ReportLevel(float level);

    /// <summary>
    ///     The latest levels, the newest last.
    /// </summary>
    IReadOnlyList<float> RecentLevels(int count);

    /// <summary>
    ///     Whether the window Echo runs in is the one the user is typing into: dictated text must not
    ///     be typed into Echo's own command line.
    /// </summary>
    bool IsEchoFocused();
}