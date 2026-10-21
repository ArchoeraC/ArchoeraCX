using System.Net;
using System.Net.Http.Headers;
using System.Text;

namespace Archoera.Tests;

/// <summary>
/// Deterministic in-memory transport. Records request URIs/user agents and
/// replays queued responses; tests never touch the network.
/// </summary>
internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpResponseMessage>> _responses = new();

    public List<CapturedRequest> Requests { get; } = [];

    public void EnqueueJson(string json, HttpStatusCode status = HttpStatusCode.OK)
        => Enqueue(() => new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        });

    public void Enqueue(Func<HttpResponseMessage> response)
    {
        _responses.Enqueue(response);
    }

    public void EnqueueStatus(
        HttpStatusCode status,
        int? retryAfterSeconds = null,
        int? rateLimitRemaining = null)
        => Enqueue(() =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(string.Empty, Encoding.UTF8, "application/json"),
            };

            if (retryAfterSeconds is { } seconds)
            {
                response.Headers.RetryAfter =
                    new RetryConditionHeaderValue(TimeSpan.FromSeconds(seconds));
            }

            if (rateLimitRemaining is { } remaining)
            {
                response.Headers.TryAddWithoutValidation("X-Ratelimit-Limit", "300");
                response.Headers.TryAddWithoutValidation(
                    "X-Ratelimit-Remaining",
                    remaining.ToString(System.Globalization.CultureInfo.InvariantCulture));
                response.Headers.TryAddWithoutValidation("X-Ratelimit-Reset", "60");
            }

            return response;
        });

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        Requests.Add(new CapturedRequest(
            request.RequestUri ?? new Uri("about:blank"),
            request.Method.Method,
            request.Headers.TryGetValues("User-Agent", out var agents)
                ? string.Join(' ', agents)
                : null));

        if (_responses.Count == 0)
        {
            throw new InvalidOperationException(
                $"No fake response queued for {request.RequestUri}");
        }

        return Task.FromResult(_responses.Dequeue()());
    }

    internal sealed record CapturedRequest(Uri Uri, string Method, string? UserAgent);
}
