using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Echo.Tests.IntegrationTests;

/// <summary>
///     The releases of Echo on GitHub, played on this computer over real HTTP. The API names the latest release and
///     its files; a download is redirected to the storage, as GitHub redirects it; the storage gives a file whole, or
///     the range of its bytes a request asks for.
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
    ///     How many bytes of files the storage has sent.
    /// </summary>
    public long BytesSent => Interlocked.Read(ref _bytesSent);

    /// <summary>
    ///     Whether the storage sends whole files only, as some servers do.
    /// </summary>
    public bool IgnoresRanges { get; set; }

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
                (string path, string? range) = await ReadRequestAsync(stream);

                string version;
                Dictionary<string, byte[]> files;

                lock (_lock)
                {
                    version = _version;
                    files = _files;
                }

                if (path == LatestRelease.AbsolutePath)
                {
                    await WriteAsync(stream, "200 OK", [], Release(version, files));
                }
                else if (path.StartsWith($"/releases/download/v{version}/", StringComparison.Ordinal))
                {
                    await WriteAsync(stream, "302 Found", [$"Location: {Root}/storage/{path.Split('/')[^1]}?signature=1"], Array.Empty<byte>());
                }
                else if (path.StartsWith("/storage/", StringComparison.Ordinal) && files.TryGetValue(path["/storage/".Length..], out byte[]? file))
                {
                    await WriteFileAsync(stream, file, IgnoresRanges ? null : range);
                }
                else
                {
                    await WriteAsync(stream, "404 Not Found", [], Array.Empty<byte>());
                }
            }
            catch (IOException)
            {
                // The client has what it needed, and closed the connection before the end of a file
            }
        }
    }

    private static async Task<(string Path, string? Range)> ReadRequestAsync(Stream stream)
    {
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

        // "GET /storage/echooo-win-x64.zip?signature=1 HTTP/1.1"
        string target = (await reader.ReadLineAsync())?.Split(' ')[1] ?? string.Empty;
        string? range = null;
        string? header;

        while (!string.IsNullOrEmpty(header = await reader.ReadLineAsync()))
        {
            if (header.StartsWith("Range: bytes=", StringComparison.OrdinalIgnoreCase))
                range = header["Range: bytes=".Length..];
        }

        return (target.Split('?')[0], range);
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
    private async Task WriteFileAsync(Stream stream, byte[] file, string? range)
    {
        if (range is null)
        {
            await WriteAsync(stream, "200 OK", [], file);
            Interlocked.Add(ref _bytesSent, file.Length);

            return;
        }

        string[] bounds = range.Split('-');
        int first = int.Parse(bounds[0]);
        int last = bounds[1].Length > 0 ? Math.Min(int.Parse(bounds[1]), file.Length - 1) : file.Length - 1;

        await WriteAsync(stream, "206 Partial Content", [$"Content-Range: bytes {first}-{last}/{file.Length}"], file.AsMemory(first, last - first + 1));
        Interlocked.Add(ref _bytesSent, last - first + 1);
    }

    private static async Task WriteAsync(Stream stream, string status, IEnumerable<string> headers, ReadOnlyMemory<byte> body)
    {
        var head = new StringBuilder($"HTTP/1.1 {status}\r\n");

        foreach (string header in headers)
            head.Append(header).Append("\r\n");

        head.Append($"Content-Length: {body.Length}\r\nConnection: close\r\n\r\n");

        await stream.WriteAsync(Encoding.ASCII.GetBytes(head.ToString()));
        await stream.WriteAsync(body);
    }
}