namespace Echo.Setup.Check.Contracts;

/// <summary>
///     Runs the real dictation pipeline with settings that have not been saved yet.
/// </summary>
internal interface IDictationTester
{
    /// <summary>
    ///     Opens the microphone, loads the model and starts listening for the push-to-talk key.
    /// </summary>
    /// <exception cref="DictationTestException">Something in the chain does not work.</exception>
    Task<IDictationTestSession> StartAsync(SetupContext draft, CancellationToken cancellationToken);
}