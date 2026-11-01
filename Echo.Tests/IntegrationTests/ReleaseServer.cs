using System.Net.Sockets;

namespace Echo.Tests.IntegrationTests;

/// <summary>
///     The releases of Echo on GitHub, played on this computer over real HTTP. The API names the latest release and
///     its files; the page of the latest release redirects to the page of its tag, as the install scripts expect; a
///     download is redirected to the storage, as GitHub redirects it; the storage gives a file whole, or the range of
///     its bytes a request asks for.
/// </summary>
internal sealed class ReleaseServer : IDisposable
{
    private readonly TcpListener _listener = new(IPAddress.Loopback, 0);
    private readonly CancellationTokenSource _stopping = new();
    private readonly object _lock = new();
    private readonly Task _serving;

    private string _version = "0.0.0";
    private Dictionary<string, byte[]> _files = [];
    private long _bytesSent;

    public ReleaseServer()
    {
        _listener.Start();
        _serving = ServeAsync();
    }

    /// <summary>
    ///     Where the API describes the latest release.
    /// </summary>
    public Uri LatestRelease => new($"{Root}/repos/GithubPhobos/Echo/releases/latest");

    /// <summary>
    ///     The pages of the releases, as the install scripts take them: "https://github.com/GithubPhobos/Echo/releases".
    /// </summary>
    public string Releases => $"{Root}/releases";

    /// <summary>
    ///     How many bytes of files the storage has sent.
    /// </summary>
    public long BytesSent => Interlocked.Read(ref _bytesSent);

    /// <summary>
    ///     Whether the storage sends whole files only, as some servers do.
    /// </summary>
    public bool IgnoresRanges { get; set; }

    /// <summary>
    ///     The file the storage stops sending halfway through, keeping the connection open, as a stalled server does.
    /// </summary>
    public string? StallsOn { get; set; }

    private string Root => $"http://127.0.0.1:{((IPEndPoint)_listener.LocalEndpoint).Port}";

    /// <summary>
    ///     Publishes a release with these files and SHA256SUMS with their checksums: the latest release from now on.
    /// </summary>
    public void Publish(string version, IReadOnlyDictionary<string, byte[]> files)
    {
        var published = new Dictionary<string, byte[]>(files)
        {
            ["SHA256SUMS"] = Encoding.UTF8.GetBytes(string.Concat(files.Select(file =>
                $"{Convert.ToHexString(SHA256.HashData(file.Value)).ToLowerInvariant()}  {file.Key}\n")))
        };

        lock (_lock)
        {
            _version = version;
            _files = published;
        }
    }

    /// <summary>
    ///     Changes a byte of a published file, and not its checksum, as damage on the way would.
    /// </summary>
    public void Damage(string name, int position)
    {
        lock (_lock)
            _files[name][position] ^= 0xFF;
    }

    public void Dispose()
    {
        _stopping.Cancel();
        _listener.Stop();
        _serving.Wait();
    }

    private async Task ServeAsync()
    {
        while (true)
        {
            TcpClient client;

            try
            {
                client = await _listener.AcceptTcpClientAsync(_stopping.Token);
            }
            catch (Exception exc) when (exc is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            _ = Task.Run(() => AnswerAsync(client));
        }
    }

    /// <summary>
    ///     Answers one request and closes the connection.
    /// </summary>
    private async Task AnswerAsync(TcpClient client)
    {
        using (client)
        {
            try
            {
                NetworkStream stream = client.GetStream();
                (string method, string path, string? range) = await ReadRequestAsync(stream);

                // A request for the headers alone, as the install scripts make to follow redirects
                bool isHead = method == "HEAD";

                string version;
                Dictionary<string, byte[]> files;

                lock (_lock)
                {
                    version = _version;
                    files = _files;
                }

                if (path == LatestRelease.AbsolutePath)
                {
                    await WriteAsync(stream, isHead, "200 OK", [], Release(version, files));
                }
                else if (path == "/releases/latest")
                {
                    await WriteAsync(stream, isHead, "302 Found", [$"Location: {Releases}/tag/v{version}"], Array.Empty<byte>());
                }
                else if (path == $"/releases/tag/v{version}")
                {
                    await WriteAsync(stream, isHead, "200 OK", [], Encoding.UTF8.GetBytes($"Echo {version}"));
                }
                else if (path.StartsWith($"/releases/download/v{version}/", StringComparison.Ordinal))
                {
                    await WriteAsync(stream, isHead, "302 Found", [$"Location: {Root}/storage/{path.Split('/')[^1]}?signature=1"], Array.Empty<byte>());
                }
                else if (path.StartsWith("/storage/", StringComparison.Ordinal) && files.TryGetValue(path["/storage/".Length..], out byte[]? file))
                {
                    await WriteFileAsync(stream, isHead, file, IgnoresRanges ? null : range, path.EndsWith($"/{StallsOn}", StringComparison.Ordinal));
                }
                else
                {
                    await WriteAsync(stream, isHead, "404 Not Found", [], Array.Empty<byte>());
                }
            }
            catch (Exception exc) when (exc is IOException or OperationCanceledException)
            {
                // The client has what it needed and closed the connection before the end of a file, or the server
                // stopped while it stalled
            }
        }
    }

    private static async Task<(string Method, string Path, string? Range)> ReadRequestAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

        // "GET /storage/echooo-win-x64.zip?signature=1 HTTP/1.1"
        string[] line = (await reader.ReadLineAsync())?.Split(' ') ?? [string.Empty, string.Empty];
        string? range = null;
        string? header;

        while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync()))
        {
            if (header.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase))
                range = header["Range: bytes=".Length..];
        }

        return (line[0], line[1].Split('?')[0], range);
    }

    /// <summary>
    ///     The answer of the API about the latest release: its tag and its files.
    /// </summary>
    private byte[] Release(string version, Dictionary<string, byte[]> files) => JsonSerializer.SerializeToUtf8Bytes(new
    {
        tag_name = $"v{version}",
        assets = files.Select(file => new
        {
            name = file.Key,
            size = file.Value.Length,
            browser_download_url = $"{Root}/releases/download/v{version}/{file.Key}"
        })
    });

    /// <summary>
    ///     Sends the file, or the bytes "first-last" of it.
    /// </summary>
    /// <param name="stalls">Whether only half of it is sent, and then nothing until the server stops.</param>
    private async Task WriteFileAsync(Stream stream, bool isHead, byte[] file, string? range, bool stalls)
    {
        string status = "200 OK";
        List<string> headers = [];
        ReadOnlyMemory<byte> body = file;

        if (range is not null)
        {
            string[] bounds = range.Split('-');
            int first = int.Parse(bounds[0]);
            int last = bounds[1].Length > 0 ? Math.Min(int.Parse(bounds[1]), file.Length - 1) : file.Length - 1;

            status = "206 Partial Content";
            headers.Add($"Content-Range: bytes {first}-{last}/{file.Length}");
            body = file.AsMemory(first, last - first + 1);
        }

        if (!stalls || isHead)
        {
            await WriteAsync(stream, isHead, status, headers, body);
            Interlocked.Add(ref _bytesSent, isHead ? 0 : body.Length);

            return;
        }

        await WriteHeadAsync(stream, status, headers, body.Length);
        await stream.WriteAsync(body[..(body.Length / 2)]);
        await stream.FlushAsync();
        Interlocked.Add(ref _bytesSent, body.Length / 2);

        await Task.Delay(Timeout.Infinite, _stopping.Token);
    }

    /// <summary>
    ///     Sends an answer; to a request for the headers alone, without its body.
    /// </summary>
    private static async Task WriteAsync(Stream stream, bool isHead, string status, IEnumerable<string> headers, ReadOnlyMemory<byte> body)
    {
        await WriteHeadAsync(stream, status, headers, body.Length);

        if (!isHead)
            await stream.WriteAsync(body);
    }

    private static async Task WriteHeadAsync(Stream stream, string status, IEnumerable<string> headers, int length)
    {
        var head = new StringBuilder($"HTTP/1.1 {status}\r\n");

        foreach (string header in headers)
            head.Append(header).Append("\r\n");

        head.Append($"Content-Length: {length}\r\nConnection: close\r\n\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()));
    }
}