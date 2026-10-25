namespace Echo.Tests.UnitTests;

/// <summary>
///     The body of an answer that comes slowly: in four pieces with a pause before each, or, from a stalled server,
///     half of it and then nothing while the connection stays open.
/// </summary>
/// <param name="bytes">The whole body.</param>
/// <param name="pause">The pause before each piece.</param>
/// <param name="stalls">Whether only the first half comes.</param>
internal sealed class SlowBody(byte[] bytes, TimeSpan pause, bool stalls) : Stream
{
    private readonly byte[] _bytes = bytes;
    private readonly TimeSpan _pause = pause;
    private readonly bool _stalls = stalls;

    private int _position;

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        int end = _stalls ? _bytes.Length / 2 : _bytes.Length;

        if (_position >= end)
        {
            if (!_stalls)
                return 0;

            // Nothing more comes, until the reader gives up
            await Task.Delay(Timeout.Infinite, cancellationToken);
        }

        await Task.Delay(_pause, cancellationToken);

        int count = Math.Min(Math.Min(buffer.Length, _bytes.Length / 4 + 1), end - _position);

        _bytes.AsMemory(_position, count).CopyTo(buffer);
        _position += count;

        return count;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override int Read(byte[] buffer, int offset, int count) =>
        ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
}