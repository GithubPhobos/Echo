namespace Echo.Setup.Downloads;

/// <summary>
///     Copies the files the user brings in from elsewhere on the computer.
/// </summary>
internal static class FileCopier
{
    /// <summary>
    ///     Differs from the suffix of unfinished downloads, so a copy never overwrites a download that can be continued.
    /// </summary>
    private const string PartialSuffix = ".copy";

    private const int BufferSize = 1024 * 1024;

    /// <summary>
    ///     Copies a file so that an interrupted copy never looks like a finished file.
    /// </summary>
    /// <param name="source">The file to copy.</param>
    /// <param name="target">Where the copy belongs.</param>
    /// <param name="progress">Told how much is done.</param>
    /// <param name="cancellationToken"><see cref="CancellationToken"/></param>
    public static async Task CopyAsync(string source, string target, IProgress<DownloadProgress> progress, CancellationToken cancellationToken)
    {
        string partial = target + PartialSuffix;

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        try
        {
            await using (var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true))
            await using (var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, BufferSize, useAsync: true))
            {
                byte[] buffer = new byte[BufferSize];
                var clock = Stopwatch.StartNew();
                long done = 0;
                int read;

                while ((read = await input.ReadAsync(buffer, cancellationToken)) > 0)
                {
                    await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

                    done += read;
                    progress.Report(new DownloadProgress(done, input.Length, done / Math.Max(0.001, clock.Elapsed.TotalSeconds), IsVerifying: false));
                }
            }

            File.Move(partial, target, overwrite: true);
        }
        finally
        {
            File.Delete(partial);
        }
    }
}