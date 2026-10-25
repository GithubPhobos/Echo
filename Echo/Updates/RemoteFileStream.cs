using System.Net;
using System.Net.Http.Headers;

namespace Echo.Updates;

/// <summary>
///     A file on a server, read as if it lay on the disk: whatever is read is downloaded then, with a request for just
///     that range of bytes. Reading on from where the last part ended downloads larger and larger parts, so a large
///     file takes few requests and a small one wastes little.
/// </summary>
/// <param name="httpClient">The client the parts are downloaded with.</param>
/// <param name="address">Where the file is.</param>
/// <param name="length">The size of the file in bytes.</param>
/// <param name="stallTimeout">How long the server may send nothing before the file is given up on.</param>
internal sealed class RemoteFileStream(HttpClient httpClient, Uri address, long length, TimeSpan stallTimeout) : Stream
{
    /// <summary>
    ///     The smallest part downloaded at once. As much is downloaded of the end of the file at the first read there:
    ///     a zip archive keeps its table of contents at the end, and is read from there backwards first.
    /// </summary>
    internal const int SmallestPart = 64 * 1024;

    internal const int LargestPart = 8 * 1024 * 1024;

    private readonly HttpClient _httpClient = httpClient;
    private readonly Uri _address = address;
    private readonly long _length = length;
    private readonly TimeSpan _stallTimeout = stallTimeout;

    /// <summary>
    ///     The part read last, and where it starts in the file.
    /// </summary>
    private byte[] _part = [];
    private long _partStart;

    /// <summary>
    ///     The end of the file, once it is read.
    /// </summary>
    private byte[] _end = [];

    private int _partSize = SmallestPart;
    private long _position;

    /// <summary>
    ///     How many bytes have been downloaded so far.
    /// </summary>
    public long Downloaded { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => true;

    public override bool CanWrite => false;

    public override long Length => _length;

    public override long Position
    {
        get => _position;
        set => Seek(value, SeekOrigin.Begin);
    }

    /// <summary>
    ///     Where the end of the file, downloaded as one part, starts.
    /// </summary>
    private long EndStart => Math.Max(0, _length - SmallestPart);

    /// <inheritdoc/>
    /// <exception cref="HttpRequestException">The server could not be reached, or did not give the part asked for.</exception>
    /// <exception cref="IOException">The connection was lost.</exception>
    /// <exception cref="TimeoutException">The server stopped sending.</exception>
    public override int Read(Span<byte> buffer)
    {
        if (_position >= _length || buffer.IsEmpty)
            return 0;

        ReadOnlySpan<byte> available = Available();
        int count = Math.Min(buffer.Length, available.Length);

        available[..count].CopyTo(buffer);
        _position += count;

        return count;
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin)
    {
        long position = origin switch
        {
            SeekOrigin.Begin => offset,
            SeekOrigin.Current => _position + offset,
            SeekOrigin.End => _length + offset,
            _ => throw new ArgumentOutOfRangeException(nameof(origin))
        };

        if (position < 0)
            throw new IOException("A place before the start of the file cannot be read.");

        _position = position;

        return position;
    }

    public override void Flush()
    {
    }

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();

    /// <summary>
    ///     The bytes from the current position to the end of the part that holds it, downloaded when no part does.
    /// </summary>
    private ReadOnlySpan<byte> Available()
    {
        if (_position >= EndStart)
        {
            if (_end.Length == 0)
                _end = Download(EndStart, _length - EndStart);

            return _end.AsSpan((int)(_position - EndStart));
        }

        if (_position >= _partStart && _position < _partStart + _part.Length)
            return _part.AsSpan((int)(_position - _partStart));

        // Reading on where the last part ended takes a part twice as large; reading elsewhere starts small again
        _partSize = _part.Length > 0 && _position == _partStart + _part.Length
            ? Math.Min(_partSize * 2, LargestPart)
            : SmallestPart;

        _partStart = _position;
        _part = Download(_position, Math.Min(_partSize, EndStart - _position));

        return _part;
    }

    private byte[] Download(long start, long count)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, _address);
        request.Headers.Range = new RangeHeaderValue(start, start + count - 1);

        // A server that stops sending and keeps the connection open is given up on, as a lost connection is;
        // the time counts anew with every piece that comes
        using var stalled = new CancellationTokenSource(_stallTimeout);

        try
        {
            using HttpResponseMessage response = _httpClient.Send(request, HttpCompletionOption.ResponseHeadersRead, stalled.Token);

            // A server that does not give parts answers with the whole file, or with an error
            if (response.StatusCode != HttpStatusCode.PartialContent
                || response.Content.Headers.ContentRange is not { From: { } from } range
                || from != start
                || (range.Length is { } total && total != _length))
            {
                throw new HttpRequestException(
                    $"{_address.Host} did not give bytes {start}-{start + count - 1} of {Path.GetFileName(_address.AbsolutePath)}: "
                    + $"it answered {(int)response.StatusCode}.");
            }

            byte[] part = new byte[count];
            int done = 0;

            using Stream body = response.Content.ReadAsStream(stalled.Token);

            while (done < count)
            {
                // Only an asynchronous read of the answer stops waiting when it is cancelled
                int read = body.ReadAsync(part.AsMemory(done), stalled.Token).AsTask().GetAwaiter().GetResult();

                if (read == 0)
                    throw new EndOfStreamException($"{_address.Host} ended the connection after {done} of {count} bytes.");

                done += read;
                stalled.CancelAfter(_stallTimeout);
            }

            Downloaded += count;

            return part;
        }
        catch (Exception exc) when ((exc is OperationCanceledException or IOException) && stalled.IsCancellationRequested)
        {
            throw new TimeoutException($"{_address.Host} sent nothing for {_stallTimeout.TotalSeconds} seconds.", exc);
        }
    }
}