using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Options;
using XtreamForge.ApiService.Services.Queues;
using XtreamForge.ApiService.Services.Tmdb;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Tmdb;

namespace XtreamForge.ApiService.Services.TmdbInfos;

/// <summary>
/// Reads and stores the TMDB metadata used to enrich the Xtream items.
/// Loaded metadata is refreshed after a random delay between <see cref="MinimumRefreshDelay"/> and <see cref="MaximumRefreshDelay"/>;
/// a load without result or failing definitively is retried after a delay
/// doubling from <see cref="FirstRetryDelay"/> up to <see cref="MaximumRetryDelay"/>, a transient failure (TMDB outage, timeout, HTTP 429 or 5xx)
/// after <see cref="RetryDelay.TransientFailureDelay"/>.
/// </summary>
public class TmdbInfoService(XtreamForgeDbContext dbContext, TmdbClient tmdbClient, IOptions<TmdbOptions> options, TimeProvider timeProvider)
    : IQueueProcessor<TmdbInfoRequest>
{
    public static readonly TimeSpan MinimumRefreshDelay = TimeSpan.FromDays(50);
    public static readonly TimeSpan MaximumRefreshDelay = TimeSpan.FromDays(180);
    public static readonly TimeSpan FirstRetryDelay = TimeSpan.FromDays(1);
    public static readonly TimeSpan MaximumRetryDelay = TimeSpan.FromDays(30);

    private const int MaximumTextLength = 500;
    private const int MaximumNameLength = 200;
    private const int MaximumCastCount = 10;
    private const int MaximumDirectorCount = 10;

    // longer than any movie or episode: a larger value is a TMDB data error
    private const int MaximumDurationMinutes = 1440;

    Task<bool> IQueueProcessor<TmdbInfoRequest>.ProcessAsync(TmdbInfoRequest request, CancellationToken cancellationToken)
        => LoadAsync(request, cancellationToken);

    /// <summary>
    /// Returns the stored metadata (loaded or not) of the given TMDB IDs that are not excluded manually, and the IDs of the excluded ones;
    /// unknown IDs are absent from both.
    /// </summary>
    public async Task<TmdbInfoLookup> GetAsync(ContentType type, IReadOnlyCollection<long> tmdbIds, CancellationToken cancellationToken)
    {
        if (tmdbIds.Count == 0)
            return TmdbInfoLookup.Empty;

        var infos = await dbContext.TmdbInfos
            .AsNoTracking()
            .Where(info => info.ContentType == type && !info.IsExcluded && tmdbIds.Contains(info.TmdbId))
            .ToDictionaryAsync(info => info.TmdbId, cancellationToken);

        var excludedTmdbIds = await dbContext.TmdbInfos
            .Where(info => info.ContentType == type && info.IsExcluded && tmdbIds.Contains(info.TmdbId))
            .Select(info => info.TmdbId)
            .ToListAsync(cancellationToken);

        return new TmdbInfoLookup(infos, excludedTmdbIds.ToHashSet());
    }

    /// <summary>
    /// Loads the metadata of a TMDB ID from TMDB; returns true when it was loaded, false when no API key is configured, the load is not due yet
    /// (the request is then ignored: metadata already loaded and not due for a refresh, or a failed load waiting for its retry), or TMDB does not know the ID.
    /// </summary>
    public async Task<bool> LoadAsync(TmdbInfoRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(options.Value.ApiKey))
            return false;

        var info = await dbContext.TmdbInfos.SingleOrDefaultAsync(info => info.ContentType == request.Type && info.TmdbId == request.TmdbId, cancellationToken);
        if (info is not null && info.NextLoadAtUtc > timeProvider.GetUtcNow())
            return false;

        info ??= dbContext.TmdbInfos.Add(new TmdbInfo { ContentType = request.Type, TmdbId = request.TmdbId }).Entity;

        TmdbInfoData? data;
        try
        {
            data = await tmdbClient.GetInfoAsync(request.Type, request.TmdbId, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            // a failing load is deferred, so that TMDB is not called on every catalogue request; loaded metadata is kept
            if (RetryDelay.IsTransient(exception))
                await DeferNextLoadAfterTransientFailureAsync(info, cancellationToken);
            else
                await DeferNextLoadAsync(info, cancellationToken);

            throw;
        }

        if (data is null)
        {
            await DeferNextLoadAsync(info, cancellationToken);
            return false;
        }

        Apply(info, data, timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    private async Task DeferNextLoadAsync(TmdbInfo info, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        info.LoadAttemptCount++;
        info.NextLoadAtUtc = now + RetryDelay.Get(info.LoadAttemptCount, FirstRetryDelay, MaximumRetryDelay);
        info.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // an outage says nothing about the TMDB ID: the attempt is not counted, so that it does not lengthen the next delays
    private async Task DeferNextLoadAfterTransientFailureAsync(TmdbInfo info, CancellationToken cancellationToken)
    {
        var now = timeProvider.GetUtcNow();

        info.NextLoadAtUtc = now + RetryDelay.TransientFailureDelay;
        info.UpdatedAtUtc = now;
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    // TMDB values are untrusted: blank texts are dropped, long texts truncated, and out-of-range values ignored
    private static void Apply(TmdbInfo info, TmdbInfoData data, DateTimeOffset now)
    {
        info.Title = CleanText(data.Title, MaximumTextLength);
        info.OriginalTitle = CleanText(data.OriginalTitle, MaximumTextLength);
        info.ReleaseDate = data.ReleaseDate;
        info.PosterPath = CleanText(data.PosterPath, MaximumTextLength);
        info.Overview = CleanText(data.Overview, maximumLength: null);
        info.VoteAverage = data.VoteAverage is >= 0 and <= 10 ? data.VoteAverage : null;
        info.VoteCount = data.VoteCount is >= 0 ? data.VoteCount : null;
        info.GenreIds = [.. data.GenreIds.Where(genreId => genreId > 0).Distinct()];
        info.Genres = [.. info.GenreIds.Select(TmdbGenres.GetEnglishName).OfType<string>()];
        info.Directors = CleanNames(data.Directors, MaximumDirectorCount);
        info.Cast = CleanNames(data.Cast, MaximumCastCount);
        info.DurationMinutes = data.DurationMinutes is > 0 and <= MaximumDurationMinutes ? data.DurationMinutes : null;
        info.LoadedAtUtc = now;
        info.LoadAttemptCount = 0;
        info.NextLoadAtUtc = now + GetRefreshDelay();
        info.UpdatedAtUtc = now;
    }

    // metadata loaded together (a whole catalogue) is refreshed over several months instead of all on the same day
    private static TimeSpan GetRefreshDelay()
        => TimeSpan.FromTicks(Random.Shared.NextInt64(MinimumRefreshDelay.Ticks, MaximumRefreshDelay.Ticks));

    private static string? CleanText(string? value, int? maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var text = value.Trim();

        return maximumLength is not null && text.Length > maximumLength ? text[..maximumLength.Value] : text;
    }

    private static List<string> CleanNames(IEnumerable<string> names, int maximumCount)
        => [.. names
            .Select(name => CleanText(name, MaximumNameLength))
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .Take(maximumCount)];
}
