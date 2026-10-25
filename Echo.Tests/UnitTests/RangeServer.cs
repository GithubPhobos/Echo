using System.Net;
using System.Net.Http.Headers;

namespace Echo.Tests.UnitTests;

/// <summary>
///     A server that holds one file and gives any range of its bytes, as GitHub does for the files of a release,
///     unless it is told to send the whole file every time, as some servers do.
/// </summary>
internal sealed class RangeServer(byte[] content) : HttpMessageHandler
{
    private readonly byte[] _content = content;

    /// <summary>
    ///     The ranges asked for, first byte and last byte, in the order they were asked.
    /// </summary>
    public List<(long From, long To)> Requests { get; } = [];

    /// <summary>
    ///     How many bytes the server has sent.
    /// </summary>
    public long BytesSent { get; private set; }

    public bool IgnoresRanges { get; set; }

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (IgnoresRanges || request.Headers.Range?.Ranges.Single() is not { From: { } from } range)
        {
            BytesSent += _content.Length;

            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(_content) };
        }

        long to = Math.Min(range.To ?? _content.Length - 1, _content.Length - 1);

        Requests.Add((from, to));
        BytesSent += to - from + 1;

        var response = new HttpResponseMessage(HttpStatusCode.PartialContent)
        {
            Content = new ByteArrayContent(_content, (int)from, (int)(to - from + 1))
        };

        response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, to, _content.Length);

        return response;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
        Task.FromResult(Send(request, cancellationToken));
}