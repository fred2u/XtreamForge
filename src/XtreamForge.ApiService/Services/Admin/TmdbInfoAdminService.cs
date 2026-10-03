using Microsoft.EntityFrameworkCore;
using System.Globalization;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;
using XtreamForge.Domain.Tmdb;

namespace XtreamForge.ApiService.Services.Admin;

/// <summary>Filters of the TMDB metadata list; a null filter keeps every entry.</summary>
public sealed record TmdbInfoListQuery(
    ContentType ContentType,
    string? Search = null,
    string? Genre = null,
    InclusionDecision? Decision = null,
    bool? IsExcluded = null,
    bool? IsLoaded = null,
    int Skip = 0,
    int Take = TmdbInfoAdminService.DefaultPageSize);

/// <summary>
/// A page of TMDB metadata with the decision of each entry, and counters and genres (sorted names) over every entry of the content type.
/// </summary>
public sealed record TmdbInfoPage(
    IReadOnlyList<TmdbRuleEvaluation> Items,
    int MatchingCount,
    int TotalCount,
    int ExcludedCount,
    int ManuallyExcludedCount,
    int NotLoadedCount,
    IReadOnlyList<string> Genres);

public class TmdbInfoAdminService(XtreamForgeDbContext dbContext)
{
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 200;

    /// <summary>
    /// Returns a page of the TMDB metadata of a content type, sorted by title, with the decision of the manual exclusion and the TMDB rules.
    /// The decision depends on every rule, so the entries of the content type are evaluated in memory, with only the listed columns
    /// (no overview, directors, or cast); the search matches the title, the original title, or the exact TMDB ID, and the genre
    /// one of the genre names, ignoring case.
    /// </summary>
    public async Task<TmdbInfoPage> GetPageAsync(TmdbInfoListQuery query, CancellationToken cancellationToken = default)
    {
        var rules = await dbContext.TmdbRules
            .AsNoTracking()
            .Where(rule => rule.ContentType == query.ContentType)
            .ToListAsync(cancellationToken);

        var infos = await dbContext.TmdbInfos
            .AsNoTracking()
            .Where(info => info.ContentType == query.ContentType)
            .Select(info => new TmdbInfo
            {
                Id = info.Id,
                TmdbId = info.TmdbId,
                ContentType = info.ContentType,
                Title = info.Title,
                OriginalTitle = info.OriginalTitle,
                ReleaseDate = info.ReleaseDate,
                PosterPath = info.PosterPath,
                VoteAverage = info.VoteAverage,
                VoteCount = info.VoteCount,
                GenreIds = info.GenreIds,
                Genres = info.Genres,
                IsExcluded = info.IsExcluded,
                LoadedAtUtc = info.LoadedAtUtc,
                LoadAttemptCount = info.LoadAttemptCount,
                NextLoadAtUtc = info.NextLoadAtUtc
            })
            .ToListAsync(cancellationToken);

        var evaluations = TmdbRuleService.Evaluate(infos, rules).ToList();

        var matching = evaluations
            .Where(evaluation => Matches(evaluation, query))
            .OrderBy(evaluation => evaluation.Info.Title ?? string.Empty, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(evaluation => evaluation.Info.TmdbId)
            .ToList();

        var take = Math.Clamp(query.Take, 1, MaximumPageSize);
        var skip = Math.Max(query.Skip, 0);

        return new TmdbInfoPage(
            [.. matching.Skip(skip).Take(take)],
            matching.Count,
            evaluations.Count,
            evaluations.Count(evaluation => evaluation.Decision == InclusionDecision.Exclude),
            evaluations.Count(evaluation => evaluation.Info.IsExcluded),
            evaluations.Count(evaluation => evaluation.Info.LoadedAtUtc is null),
            [.. infos.SelectMany(info => info.Genres).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.CurrentCultureIgnoreCase)]);
    }


    /// <summary>Returns every value of an entry with its decision, or null when it does not exist.</summary>
    public async Task<TmdbRuleEvaluation?> GetAsync(int id, CancellationToken cancellationToken = default)
    {
        var info = await dbContext.TmdbInfos
            .AsNoTracking()
            .FirstOrDefaultAsync(info => info.Id == id, cancellationToken);

        if (info is null)
        {
            return null;
        }

        var rules = await dbContext.TmdbRules
            .AsNoTracking()
            .Where(rule => rule.ContentType == info.ContentType)
            .ToListAsync(cancellationToken);

        return TmdbRuleService.Evaluate(info, TmdbRuleService.OrderEnabled(rules));
    }

    /// <summary>Sets the manual exclusion of an entry; returns false when it does not exist.</summary>
    public async Task<bool> SetExcludedAsync(int id, bool isExcluded, CancellationToken cancellationToken = default)
    {
        var info = await dbContext.TmdbInfos
            .FirstOrDefaultAsync(info => info.Id == id, cancellationToken);

        if (info is null)
        {
            return false;
        }

        info.IsExcluded = isExcluded;
        info.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }

    private static bool Matches(TmdbRuleEvaluation evaluation, TmdbInfoListQuery query)
    {
        var info = evaluation.Info;

        return (query.Decision is null || evaluation.Decision == query.Decision)
            && (query.IsExcluded is null || info.IsExcluded == query.IsExcluded)
            && (query.IsLoaded is null || (info.LoadedAtUtc is not null) == query.IsLoaded)
            && MatchesGenre(info, query.Genre?.Trim())
            && MatchesSearch(info, query.Search?.Trim());
    }

    private static bool MatchesGenre(TmdbInfo info, string? genre) =>
        string.IsNullOrEmpty(genre) || info.Genres.Contains(genre, StringComparer.OrdinalIgnoreCase);

    private static bool MatchesSearch(TmdbInfo info, string? search)
    {
        if (string.IsNullOrEmpty(search))
            return true;

        return (info.Title?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false)
            || (info.OriginalTitle?.Contains(search, StringComparison.CurrentCultureIgnoreCase) ?? false)
            || (long.TryParse(search, NumberStyles.None, CultureInfo.InvariantCulture, out var tmdbId) && info.TmdbId == tmdbId);
    }
}
