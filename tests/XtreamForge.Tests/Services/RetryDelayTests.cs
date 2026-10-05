using System.Text.Json;
using Polly.CircuitBreaker;
using Polly.Timeout;
using XtreamForge.ApiService.Services;

namespace XtreamForge.Tests.Services;

public class RetryDelayTests
{
    public static TheoryData<Exception, bool> Failures => new()
    {
        { new HttpRequestException("Connection refused."), true },
        { new HttpRequestException("Service unavailable.", null, HttpStatusCode.ServiceUnavailable), true },
        { new HttpRequestException("Too many requests.", null, HttpStatusCode.TooManyRequests), true },
        { new HttpRequestException("Request timeout.", null, HttpStatusCode.RequestTimeout), true },
        { new TaskCanceledException("The request timed out."), true },
        { new TimeoutRejectedException(), true },
        { new BrokenCircuitException(), true },
        { new IOException("The response ended prematurely."), true },
        { new HttpRequestException("Unauthorized.", null, HttpStatusCode.Unauthorized), false },
        { new HttpRequestException("Not found.", null, HttpStatusCode.NotFound), false },
        { new JsonException("Invalid JSON."), false },
        { new InvalidOperationException("Unexpected."), false }
    };

    [Theory]
    [MemberData(nameof(Failures))]
    public void IsTransient_ClassifiesTheFailure(Exception exception, bool expected)
    {
        Assert.Equal(expected, RetryDelay.IsTransient(exception));
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(100, 30)]
    public void Get_DoublesTheDelayUpToTheMaximum(int attemptCount, int expectedDays)
    {
        Assert.Equal(TimeSpan.FromDays(expectedDays), RetryDelay.Get(attemptCount, TimeSpan.FromDays(1), TimeSpan.FromDays(30)));
    }
}
