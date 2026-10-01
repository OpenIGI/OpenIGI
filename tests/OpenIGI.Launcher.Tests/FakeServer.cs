using System.Net;
using System.Net.Http.Headers;

namespace OpenIGI.Launcher.Tests;

/// <summary>An in-memory download server: files by path, honouring Range, counting what was asked for.</summary>
internal sealed class FakeServer : HttpMessageHandler
{
    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    public List<HttpRequestMessage> Requests { get; } = [];

    /// <summary>Gets or sets bytes after which a download is cut off, to simulate a dropped connection.</summary>
    public long? CutAfter { get; set; }

    public Func<HttpRequestMessage, HttpResponseMessage?>? Override { get; set; }

    public void Add(string path, byte[] content) => _files[path] = content;

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        if (Override?.Invoke(request) is { } custom)
        {
            return Task.FromResult(custom);
        }

        if (!_files.TryGetValue(request.RequestUri!.AbsolutePath, out byte[]? content))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        long from = request.Headers.Range?.Ranges.First().From ?? 0;
        byte[] slice = content[(int)from..];
        if (CutAfter is { } cut && slice.Length > cut)
        {
            slice = slice[..(int)cut];
        }

        HttpResponseMessage response = new(from > 0 ? HttpStatusCode.PartialContent : HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(slice),
        };
        if (from > 0)
        {
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, content.Length - 1, content.Length);
        }

        return Task.FromResult(response);
    }
}
