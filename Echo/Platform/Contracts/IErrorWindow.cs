namespace Echo.Platform.Contracts;

/// <summary>
///     Tells the user about an error that stops Echo when there is no screen of its own to show it on.
/// </summary>
internal interface IErrorWindow
{
    /// <summary>
    ///     Shows the message where the system can show it. The message is in the log in any case.
    /// </summary>
    void Show(string message);
}