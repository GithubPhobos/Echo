namespace Echo.Services.Contracts;

/// <summary>
///     Listens to global keyboard events across the operating system to detect 
///     Push-To-Talk (PTT) key combinations and trigger recording states.
/// </summary>
internal interface IPushToTalkMonitorService : IDisposable
{
    /// <summary>
    ///     Fired when the user releases the configured Push-To-Talk hotkey combination.
    /// </summary>
    event EventHandler? OnRecordingReleased;

    /// <summary>
    ///     Fired when the user presses and holds the configured Push-To-Talk hotkey combination.
    /// </summary>
    event EventHandler? OnRecordingTriggered;

    /// <summary>
    ///     Parses the user-configured Push-To-Talk keys (main key and optional modifier).
    /// </summary>
    /// <exception cref="Exception">Thrown if the main key cannot be parsed from the configuration.</exception>
    void InitializePushToTalkHotkeysOrThrow();

    /// <summary>
    ///     Starts the background global keyboard hook. 
    /// </summary>
    /// <remarks>
    ///     This method runs asynchronously and listens to system-wide input events.
    /// </remarks>
    Task StartListeningAsync();
}