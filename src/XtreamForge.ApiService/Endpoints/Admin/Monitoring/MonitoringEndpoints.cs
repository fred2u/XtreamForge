using XtreamForge.ApiService.Endpoints.Admin.Monitoring.Dto;
using XtreamForge.ApiService.Infrastructure.RateLimiting;
using XtreamForge.ApiService.Services.Monitoring;

namespace XtreamForge.ApiService.Endpoints.Admin.Monitoring;

public static class MonitoringEndpoints
{
    public static IResult GetQueues(QueueMonitor queueMonitor, UpstreamRateLimiter rateLimiter)
    {
        var queues = queueMonitor.GetSnapshots()
            .Select(queue => new QueueStatusDto(
                queue.Name,
                queue.Size,
                [.. queue.Samples.Select(sample => new QueueSampleDto(sample.At, sample.Size, sample.Succeeded, sample.NoResult, sample.Failed))]))
            .ToList();

        var hosts = rateLimiter.GetHosts()
            .OrderBy(host => host.Host, StringComparer.OrdinalIgnoreCase)
            .Select(host => new RateLimitedHostDto(host.Host, host.Interval, host.RateLimitedCount))
            .ToList();

        return TypedResults.Ok(new QueuesStatusDto(QueueMonitor.SampleInterval, queues, hosts));
    }
}
