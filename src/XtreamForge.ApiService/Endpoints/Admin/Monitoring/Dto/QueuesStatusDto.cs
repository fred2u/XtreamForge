namespace XtreamForge.ApiService.Endpoints.Admin.Monitoring.Dto;

public sealed record QueuesStatusDto(
    TimeSpan SampleInterval,
    IReadOnlyList<QueueStatusDto> Queues,
    IReadOnlyList<RateLimitedHostDto> RateLimitedHosts);

/// <summary>A background queue with its current size and its history (oldest sample first).</summary>
public sealed record QueueStatusDto(
    string Name,
    int Size,
    IReadOnlyList<QueueSampleDto> Samples);

/// <summary>Size of the queue at <paramref name="At"/>, and the items processed since the previous sample by outcome.</summary>
public sealed record QueueSampleDto(
    DateTimeOffset At,
    int Size,
    int Succeeded,
    int NoResult,
    int Failed);

/// <summary>An upstream host (scheme, host and port) that answered HTTP 429 since the application started.</summary>
public sealed record RateLimitedHostDto(
    string Host,
    TimeSpan Interval,
    long RateLimitedCount);
