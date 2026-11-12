namespace DeviceLookup.Core.Services;

internal static class HttpRetry
{
    /// <summary>
    /// GET with retry for timeouts, network errors, HTTP 429 and 5xx, with growing delays.
    /// A user cancel is never retried.
    /// </summary>
    public static async Task<string> GetStringAsync(
        HttpClient http, string url, TimeSpan baseDelay, CancellationToken ct, int attempts = 3)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
                var code = (int)resp.StatusCode;
                if ((code == 429 || code >= 500) && attempt < attempts)
                {
                    await Task.Delay(Backoff(resp, baseDelay, attempt), ct).ConfigureAwait(false);
                    continue;
                }
                resp.EnsureSuccessStatusCode();
                return await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            }
            catch (HttpRequestException) when (attempt < attempts)
            {
                await Task.Delay(baseDelay * attempt, ct).ConfigureAwait(false);
            }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested && attempt < attempts)
            {
                await Task.Delay(baseDelay * attempt, ct).ConfigureAwait(false);   // timeout
            }
        }
    }

    private static TimeSpan Backoff(HttpResponseMessage resp, TimeSpan baseDelay, int attempt)
    {
        var retryAfter = resp.Headers.RetryAfter?.Delta;
        if (retryAfter is { } d && d < TimeSpan.FromSeconds(30)) return d;
        return baseDelay * attempt * 2;
    }
}
