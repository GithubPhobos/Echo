using Echo.Updates;

namespace Echo.Listening;

/// <summary>
///     Everything the working screen shows at one moment.
/// </summary>
/// <param name="State">What Echo is doing.</param>
/// <param name="RecordingTime">How long the current recording has been going.</param>
/// <param name="Clock">How long the screen has been running; moves the animations.</param>
/// <param name="Levels">The latest microphone levels, the newest last.</param>
/// <param name="Model">The short name of the model.</param>
/// <param name="Key">The push-to-talk key, the way a person names it.</param>
/// <param name="Entries">The log lines to show, the newest last.</param>
/// <param name="ScrollRows">How many rows the log is scrolled back from its end.</param>
/// <param name="Input">What is typed in the command line.</param>
/// <param name="Panel">The rows offered under the command line, if any.</param>
/// <param name="Selected">The highlighted row of the panel.</param>
internal sealed record ListenView(
    ListenState State,
    TimeSpan RecordingTime,
    TimeSpan Clock,
    IReadOnlyList<float> Levels,
    string Model,
    string Key,
    IReadOnlyList<ScreenEntry> Entries,
    int ScrollRows,
    string Input,
    IReadOnlyList<PanelRow> Panel,
    int Selected)
{
    /// <summary>
    ///     The level below which the microphone hears nothing worth recording: the silence level of the settings.
    /// </summary>
    public float SilenceLevel { get; init; } = 0.01f;

    /// <summary>
    ///     The version in use and what there is about a newer one; null leaves the version out.
    /// </summary>
    public UpdateStatus? Update { get; init; }

    /// <summary>
    ///     Whether Echo is still starting: the command line takes nothing until it is ready or could not start.
    /// </summary>
    public bool IsStarting { get; init; }
}