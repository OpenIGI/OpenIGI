namespace OpenIGI.Releases.Tests;

/// <summary>An HTTP handler that answers every request as told: a stand-in master for the account and stats clients.</summary>
/// <param name="answer">The answer to a request.</param>
internal sealed class AnsweringHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> answer) : HttpMessageHandler
{
    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => answer(request);
}
