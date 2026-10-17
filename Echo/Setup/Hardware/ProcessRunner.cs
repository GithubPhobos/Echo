namespace Echo.Setup.Hardware;

/// <inheritdoc cref="IProcessRunner"/>
internal sealed class ProcessRunner : IProcessRunner
{
    /// <inheritdoc/>
    public async Task<string?> RunAsync(string fileName, string arguments, TimeSpan timeout, CancellationToken cancellationToken)
    {
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo(fileName, arguments)
                {
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            process.Start();

            using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutSource.CancelAfter(timeout);

            try
            {
                string output = await process.StandardOutput.ReadToEndAsync(timeoutSource.Token);
                await process.WaitForExitAsync(timeoutSource.Token);

                return process.ExitCode == 0 ? output : null;
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                process.Kill(entireProcessTree: true);

                return null;
            }
        }
        catch (Exception exc) when (exc is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            // The tool is not installed
            return null;
        }
    }
}