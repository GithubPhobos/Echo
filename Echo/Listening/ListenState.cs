namespace Echo.Listening;

/// <summary>
///     What Echo is doing while it runs, as the working screen shows it.
/// </summary>
internal enum ListenState
{
    /// <summary>The model and the devices are being loaded.</summary>
    Starting,

    /// <summary>Waiting for the push-to-talk key.</summary>
    Listening,

    /// <summary>The key is held and the voice is being recorded.</summary>
    Recording,

    /// <summary>The key was released and the recording is being recognized.</summary>
    Transcribing,

    /// <summary>The key is not listened to until the user resumes.</summary>
    Paused,

    /// <summary>Echo could not start; the reason is in the log.</summary>
    Failed
}