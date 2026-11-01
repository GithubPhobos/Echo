namespace Echo.Setup.Hardware.Contracts;

/// <summary>
///     Runs an external program and captures what it prints.
/// </summary>
internal interface IProcessRunner
{
    /// <summary>
    ///     Returns the standard output, or null when the program is missing, fails or does not finish in time.
    /// </summary>
    Task<string?> RunAsync(string fileName, string arguments, TimeSpan timeout, CancellationToken cancellationToken);
}