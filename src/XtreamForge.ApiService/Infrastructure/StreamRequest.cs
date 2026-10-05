namespace XtreamForge.ApiService.Infrastructure;

/// <summary>
/// Marks the upstream requests of the media streams: they are sent at once and only once. <see cref="RateLimiting.RateLimitHandler"/>
/// neither delays nor retries them, and the standard resilience pipeline does not retry them: the player gives up after a few seconds
/// and retries by itself, so a delayed or retried stream request only keeps the provider connection of the account busy.
/// </summary>
public static class StreamRequest
{
    private static readonly HttpRequestOptionsKey<bool> StreamRequestOption = new("XtreamForge.StreamRequest");

    public static void Mark(HttpRequestMessage request)
    {
        ArgumentNullException.ThrowIfNull(request);

        request.Options.Set(StreamRequestOption, true);
    }

    public static bool IsMarked(HttpRequestMessage? request)
        => request is not null && request.Options.TryGetValue(StreamRequestOption, out var isStream) && isStream;
}
