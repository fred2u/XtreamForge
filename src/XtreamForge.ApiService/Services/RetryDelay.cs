namespace XtreamForge.ApiService.Services;

public static class RetryDelay
{
    /// <summary>
    /// Returns the delay before the next attempt: <paramref name="firstDelay"/> after the first attempt, doubled on each new attempt, up to <paramref name="maximumDelay"/>.
    /// </summary>
    public static TimeSpan Get(int attemptCount, TimeSpan firstDelay, TimeSpan maximumDelay)
    {
        // the exponent is capped so that the doubling cannot overflow; the maximum delay is reached long before
        var delay = firstDelay * Math.Pow(2, Math.Clamp(attemptCount - 1, 0, 10));

        return delay < maximumDelay ? delay : maximumDelay;
    }
}
