using XtreamForge.ApiService.Services.WatchHistory;

namespace XtreamForge.Tests.Services.WatchHistory;

public class SeriesEpisodeQueueTests
{
    private static readonly XtreamEpisode[] Episodes = [new("1001", 1, 1)];

    private readonly SeriesEpisodeQueue _queue = new();

    [Fact]
    public void TryEnqueue_WhileTheSameSeriesIsPending_ReturnsFalse()
    {
        Assert.True(_queue.TryEnqueue(new SeriesEpisodeRequest(1, "42", Episodes)));

        Assert.False(_queue.TryEnqueue(new SeriesEpisodeRequest(1, "42", Episodes)));
        Assert.True(_queue.TryEnqueue(new SeriesEpisodeRequest(1, "43", Episodes)));
        Assert.True(_queue.TryEnqueue(new SeriesEpisodeRequest(2, "42", Episodes)));
    }

    [Fact]
    public void TryEnqueue_WithoutEpisode_ReturnsFalse()
    {
        Assert.False(_queue.TryEnqueue(new SeriesEpisodeRequest(1, "42", [])));
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("12345678901234567890123456789012345678901234567890123456789012345")]
    public void TryEnqueue_WhenTheSeriesIdIsNotAStreamId_ReturnsFalse(string seriesId)
    {
        Assert.False(_queue.TryEnqueue(new SeriesEpisodeRequest(1, seriesId, Episodes)));
    }

    [Fact]
    public void Request_ToString_SummarizesTheEpisodes()
    {
        Assert.Equal(
            "SeriesEpisodeRequest { XtreamSourceId = 1, SeriesId = 42, Count = 1 }",
            new SeriesEpisodeRequest(1, "42", Episodes).ToString());
    }
}
