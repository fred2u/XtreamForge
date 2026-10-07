using XtreamForge.ApiService.Services.WatchHistory;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Domain.Enums;
using XtreamForge.Tests.Infrastructure;

namespace XtreamForge.Tests.Services.WatchHistory;

public class WatchHistoryQueueTests
{
    private static readonly XtreamVideoStream Movie = new(ContentType.Vod, "user", "secret", "42");

    private readonly SteppingTimeProvider _time = new();
    private readonly WatchHistoryQueue _queue;

    public WatchHistoryQueueTests()
    {
        _queue = new WatchHistoryQueue(_time);
    }

    [Fact]
    public async Task TrackPlayback_ForANewPlayback_EnqueuesItWithItsStartDate()
    {
        using (Track(Movie))
        {
            Assert.Equal(1, _queue.Count);
        }

        var request = await ReadAsync();
        Assert.Equal(new WatchHistoryRequest("http", "provider.example.com", 8080, Movie, _time.Now), request);
    }

    [Fact]
    public void TrackPlayback_WhileARequestOfTheSamePlaybackIsRunning_DoesNotEnqueueAgain()
    {
        using var first = Track(Movie);
        _time.Now += TimeSpan.FromHours(3);

        using var seek = Track(Movie);

        Assert.Equal(1, _queue.Count);
    }

    [Fact]
    public void TrackPlayback_ShortlyAfterTheLastRequestEnded_DoesNotEnqueueAgain()
    {
        Track(Movie).Dispose();
        _time.Now += WatchHistoryQueue.PlaybackIdleTimeout;

        Track(Movie).Dispose();

        Assert.Equal(1, _queue.Count);
    }

    [Fact]
    public void TrackPlayback_LongAfterTheLastRequestEnded_EnqueuesANewPlayback()
    {
        var first = Track(Movie);
        _time.Now += TimeSpan.FromHours(2);
        first.Dispose();
        _time.Now += WatchHistoryQueue.PlaybackIdleTimeout + TimeSpan.FromSeconds(1);

        Track(Movie).Dispose();

        Assert.Equal(2, _queue.Count);
    }

    [Fact]
    public void TrackPlayback_ForAnotherMovieOrAccount_EnqueuesAnotherPlayback()
    {
        using var first = Track(Movie);

        using var otherMovie = Track(Movie with { StreamId = "43" });
        using var otherAccount = Track(Movie with { Username = "other" });

        Assert.Equal(3, _queue.Count);
    }

    [Fact]
    public void TrackPlayback_ForAnEpisodeWithTheIdOfAMovie_EnqueuesAnotherPlayback()
    {
        using var movie = Track(Movie);

        using var episode = Track(Movie with { ContentType = ContentType.Series });

        Assert.Equal(2, _queue.Count);
    }

    [Fact]
    public void TrackPlayback_DisposedTwice_EndsTheRequestOnce()
    {
        var first = Track(Movie);
        using var second = Track(Movie);

        first.Dispose();
        first.Dispose();
        _time.Now += TimeSpan.FromHours(1);

        // the second request is still running: this request belongs to the same playback
        Track(Movie).Dispose();
        Assert.Equal(1, _queue.Count);
    }

    private IDisposable Track(XtreamVideoStream stream) => _queue.TrackPlayback("http", "provider.example.com", 8080, stream);

    private async Task<WatchHistoryRequest> ReadAsync()
    {
        await using var enumerator = _queue.ReadAllAsync(TestContext.Current.CancellationToken).GetAsyncEnumerator(TestContext.Current.CancellationToken);
        Assert.True(await enumerator.MoveNextAsync());
        return enumerator.Current;
    }
}
