using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;

namespace Echo.Setup.Downloads;

/// <inheritdoc cref="IFileDownloader"/>
/// <param name="httpClient">The client used for all downloads.</param>
/// <param name="stallTimeout">How long a server may send nothing before the download is given up on.</param>
/// <param name="time">The timer source; the system clock outside controlled tests.</param>
internal sealed class FileDownloader(HttpClient httpClient, TimeSpan stallTimeout, TimeProvider? time = null) : IFileDownloader
{
    /// <summary>
    ///     How long a server may send nothing before a download is given up on, as when the connection is lost: a
    ///     server that stops sending and keeps the connection open would be waited for forever.
    /// </summary>
    public static readonly TimeSpan StallTimeout = TimeSpan.FromSeconds(30);

    private const string PartialSuffix = ".part";
    private const int BufferSize = 128 * 1024;

    private static readonly TimeSpan _reportInterval = TimeSpan.FromMilliseconds(120);

    private readonly HttpClient _httpClient = httpClient;
    private readonly TimeSpan _stallTimeout = stallTimeout;
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <inheritdoc/>
    public async Task DownloadAsync(DownloadRequest request,
                                    string destinationPath,
                                    IProgress<DownloadProgress> progress,
                                    CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);

        string partialPath = destinationPath + PartialSuffix;

        long alreadyDownloaded = PartialLength(partialPath);

        // A partial file that is too long cannot belong to this download
        if (alreadyDownloaded > request.SizeBytes)
        {
            File.Delete(partialPath);
            alreadyDownloaded = 0;
        }

        if (alreadyDownloaded < request.SizeBytes)
            await FetchAsync(request, partialPath, alreadyDownloaded, progress, cancellationToken);

        progress.Report(new DownloadProgress(request.SizeBytes, request.SizeBytes, 0, IsVerifying: true));

        string actualSha256 = await ComputeSha256Async(partialPath, cancellationToken);
        if (!string.Equals(actualSha256, request.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            // Keeping a corrupted file would make every retry fail the same way
            File.Delete(partialPath);

            throw new DownloadException($"The downloaded file is damaged (checksum mismatch): {request.Url}");
        }

        File.Move(partialPath, destinationPath, overwrite: true);
    }

    /// <inheritdoc/>
    public long RemainingBytes(DownloadRequest request, string destinationPath)
    {
        long alreadyDownloaded = PartialLength(destinationPath + PartialSuffix);

        // A partial file that is too long is deleted, and the download starts over
        return alreadyDownloaded > request.SizeBytes ? request.SizeBytes : request.SizeBytes - alreadyDownloaded;
    }

    private static long PartialLength(string partialPath) =>
        File.Exists(partialPath) ? new FileInfo(partialPath).Length : 0;

    private async Task FetchAsync(DownloadRequest request,
                                  string partialPath,
                                  long alreadyDownloaded,
                                  IProgress<DownloadProgress> progress,
                                  CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Get, request.Url);

        if (alreadyDownloaded > 0)
            message.Headers.Range = new RangeHeaderValue(alreadyDownloaded, null);

        // Counts anew with every piece that comes
        using var idle = new CancellationTokenSource(_stallTimeout, _time);
        using var stalled = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, idle.Token);

        HttpResponseMessage response;
        try
        {
            response = await _httpClient.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, stalled.Token);
        }
        catch (HttpRequestException exc)
        {
            throw new DownloadException($"Could not reach {request.Url.Host}: {exc.Message}", exc);
        }
        catch (OperationCanceledException exc) when (!cancellationToken.IsCancellationRequested)
        {
            throw Stalled(request, exc);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new DownloadException($"{request.Url.Host} answered {(int)response.StatusCode} for {request.Url}");

            // A server that ignores the range sends the whole file again
            bool isResumed = response.StatusCode == HttpStatusCode.PartialContent;
            long done = isResumed ? alreadyDownloaded : 0;

            await using var target = new FileStream(
                partialPath,
                isResumed ? FileMode.Append : FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                BufferSize,
                useAsync: true);

            byte[] buffer = new byte[BufferSize];
            var sinceReport = Stopwatch.StartNew();
            long bytesSinceReport = 0;
            double speed = 0;

            try
            {
                await using Stream source = await response.Content.ReadAsStreamAsync(stalled.Token);

                int read;
                while ((read = await source.ReadAsync(buffer, stalled.Token)) > 0)
                {
                    idle.CancelAfter(_stallTimeout);

                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);

                    done += read;
                    bytesSinceReport += read;

                    if (sinceReport.Elapsed >= _reportInterval)
                    {
                        // Smoothed, so the number on the screen does not jump with every network burst
                        double current = bytesSinceReport / sinceReport.Elapsed.TotalSeconds;
                        speed = speed == 0 ? current : speed * 0.7 + current * 0.3;

                        progress.Report(new DownloadProgress(done, request.SizeBytes, speed, IsVerifying: false));

                        bytesSinceReport = 0;
                        sinceReport.Restart();
                    }
                }
            }
            catch (Exception exc) when ((exc is OperationCanceledException or IOException)
                                        && stalled.IsCancellationRequested
                                        && !cancellationToken.IsCancellationRequested)
            {
                throw Stalled(request, exc);
            }
            catch (IOException exc)
            {
                throw new DownloadException($"The connection to {request.Url.Host} was lost: {exc.Message}", exc);
            }

            if (done != request.SizeBytes)
                throw new DownloadException($"The download from {request.Url.Host} ended early ({done} of {request.SizeBytes} bytes).");
        }
    }

    private DownloadException Stalled(DownloadRequest request, Exception exc) =>
        new($"{request.Url.Host} sent nothing for {_stallTimeout.TotalSeconds} seconds.", exc);

    private static async Task<string> ComputeSha256Async(string path, CancellationToken cancellationToken)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, BufferSize, useAsync: true);

        byte[] hash = await SHA256.HashDataAsync(stream, cancellationToken);

        return Convert.ToHexString(hash);
    }
}