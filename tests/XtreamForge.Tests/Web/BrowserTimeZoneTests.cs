using Microsoft.JSInterop;
using XtreamForge.Web.Components.Shared;

namespace XtreamForge.Tests.Web;

public class BrowserTimeZoneTests
{
    [Fact]
    public async Task GetAsync_ReturnsTheTimeZoneOfTheBrowser()
    {
        var browserTimeZone = new BrowserTimeZone(new StubJsRuntime("Europe/Brussels"));

        var timeZone = await browserTimeZone.GetAsync(TestContext.Current.CancellationToken);

        Assert.Equal(TimeSpan.FromHours(1), timeZone.GetUtcOffset(new DateTimeOffset(2026, 1, 1, 12, 0, 0, TimeSpan.Zero)));
        Assert.Equal(TimeSpan.FromHours(2), timeZone.GetUtcOffset(new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Mars/Olympus_Mons")]
    public async Task GetAsync_WithMissingOrUnknownTimeZone_ReturnsUtc(string? id)
    {
        var browserTimeZone = new BrowserTimeZone(new StubJsRuntime(id));

        var timeZone = await browserTimeZone.GetAsync(TestContext.Current.CancellationToken);

        Assert.Same(TimeZoneInfo.Utc, timeZone);
    }

    [Fact]
    public async Task GetAsync_ReadsTheBrowserOnce()
    {
        var jsRuntime = new StubJsRuntime("Europe/Brussels");
        var browserTimeZone = new BrowserTimeZone(jsRuntime);

        var first = await browserTimeZone.GetAsync(TestContext.Current.CancellationToken);
        var second = await browserTimeZone.GetAsync(TestContext.Current.CancellationToken);

        Assert.Same(first, second);
        Assert.Equal(["xtreamForge.getTimeZone"], jsRuntime.Identifiers);
    }

    private sealed class StubJsRuntime(string? timeZoneId) : IJSRuntime
    {
        public List<string> Identifiers { get; } = [];

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args) =>
            InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            Identifiers.Add(identifier);
            // a default ValueTask is completed with a null result
            return timeZoneId is null ? default : ValueTask.FromResult((TValue)(object)timeZoneId);
        }
    }
}
