using Microsoft.Extensions.Logging;
using My.Shared.Rules;

namespace My.Client.Handlers;

public class RetryDelegatingHandler : DelegatingHandler
{
    private readonly ILogger<RetryDelegatingHandler> _logger;
    private const int MaxRetries = 3;
    private static readonly int[] DelaySeconds = [2, 4, 8];

    public RetryDelegatingHandler(ILogger<RetryDelegatingHandler> logger)
    {
        _logger = logger;
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt <= MaxRetries; attempt++)
        {
            try
            {
                var response = await base.SendAsync(request, cancellationToken);

                if (response.IsSuccessStatusCode
                    || attempt >= MaxRetries
                    || !RetryPolicy.ShouldRetry(request.Method, response.StatusCode))
                    return response;

                var delay = DelaySeconds[attempt];
                _logger.LogInformation(
                    "Server returned {StatusCode} for {Url} (attempt {Attempt}/{Max}). The server may be starting up — retrying in {Delay}s.",
                    (int)response.StatusCode, request.RequestUri?.AbsolutePath, attempt + 1, MaxRetries, delay);

                response.Dispose();
                await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
                request = await CloneRequestAsync(request);
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries)
            {
                var delay = DelaySeconds[attempt];
                _logger.LogInformation(
                    "Request to {Url} failed (attempt {Attempt}/{Max}): {Message}. The server may be starting up — retrying in {Delay}s.",
                    request.RequestUri?.AbsolutePath, attempt + 1, MaxRetries, ex.Message, delay);

                await Task.Delay(TimeSpan.FromSeconds(delay), cancellationToken);
                request = await CloneRequestAsync(request);
            }
        }

        // Unreachable, but satisfies the compiler
        throw new InvalidOperationException("Retry loop exited unexpectedly.");
    }

    private static async Task<HttpRequestMessage> CloneRequestAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        if (request.Content != null)
        {
            var content = await request.Content.ReadAsByteArrayAsync();
            clone.Content = new ByteArrayContent(content);
            foreach (var header in request.Content.Headers)
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var header in request.Headers)
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);

        foreach (var property in request.Options)
            clone.Options.TryAdd(property.Key, property.Value);

        return clone;
    }
}
