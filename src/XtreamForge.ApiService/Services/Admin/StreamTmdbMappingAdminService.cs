using Microsoft.EntityFrameworkCore;
using System.Globalization;
using XtreamForge.ApiService.Services.TmdbInfos;
using XtreamForge.Database;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Services.Admin;

/// <summary>Filters of the stream TMDB mapping list of a source; a null filter keeps every mapping.</summary>
public sealed record StreamTmdbMappingListQuery(
    ContentType ContentType,
    string? Search = null,
    bool? IsMapped = null,
    int Skip = 0,
    int Take = StreamTmdbMappingAdminService.DefaultPageSize);

/// <summary>A stream TMDB mapping with the TMDB metadata of its TMDB ID, when loaded.</summary>
public sealed record StreamTmdbMappingEntry(
    int Id,
    ContentType ContentType,
    string StreamId,
    long? TmdbId,
    int LookupAttemptCount,
    DateTimeOffset? NextLookupAtUtc,
    string? Title,
    string? OriginalTitle,
    DateOnly? ReleaseDate,
    string? PosterPath);

/// <summary>A page of stream TMDB mappings; the counters cover every mapping of the source and content type, whatever the filters.</summary>
public sealed record StreamTmdbMappingPage(
    IReadOnlyList<StreamTmdbMappingEntry> Items,
    int MatchingCount,
    int TotalCount,
    int MappedCount);

public class StreamTmdbMappingAdminService(XtreamForgeDbContext dbContext, TmdbInfoQueue tmdbInfoQueue)
{
    public const int DefaultPageSize = 50;
    public const int MaximumPageSize = 200;

    /// <summary>
    /// Returns a page of the mappings of a source and content type, the most recent first, or null when the source does not exist.
    /// The search matches the exact stream ID, the exact TMDB ID, or the title or original title of the TMDB metadata, ignoring case.
    /// </summary>
    public async Task<StreamTmdbMappingPage?> GetPageAsync(int sourceId, StreamTmdbMappingListQuery query, CancellationToken cancellationToken = default)
    {
        if (!await dbContext.XtreamSources.AnyAsync(source => source.Id == sourceId, cancellationToken))
        {
            return null;
        }

        var mappings = dbContext.StreamTmdbMappings
            .AsNoTracking()
            .Where(mapping => mapping.XtreamSourceId == sourceId && mapping.ContentType == query.ContentType);

        var totalCount = await mappings.CountAsync(cancellationToken);
        var mappedCount = await mappings.CountAsync(mapping => mapping.TmdbId != null, cancellationToken);

        if (query.IsMapped is { } isMapped)
        {
            mappings = mappings.Where(mapping => (mapping.TmdbId != null) == isMapped);
        }

        // TMDB metadata is identified by content type and TMDB ID; an anonymous type keeps the joined members translatable
        var entries = mappings.LeftJoin(
            dbContext.TmdbInfos.AsNoTracking(),
            mapping => new { mapping.ContentType, mapping.TmdbId },
            info => new { info.ContentType, TmdbId = (long?)info.TmdbId },
            (mapping, info) => new { Mapping = mapping, Info = info });

        if (query.Search?.Trim() is { Length: > 0 } search)
        {
            var loweredSearch = search.ToLowerInvariant();
            long? searchedTmdbId = long.TryParse(search, NumberStyles.None, CultureInfo.InvariantCulture, out var tmdbId) ? tmdbId : null;

            entries = entries.Where(entry => entry.Mapping.StreamId == search
                || (searchedTmdbId != null && entry.Mapping.TmdbId == searchedTmdbId)
                || (entry.Info != null && entry.Info.Title != null && entry.Info.Title.ToLower().Contains(loweredSearch))
                || (entry.Info != null && entry.Info.OriginalTitle != null && entry.Info.OriginalTitle.ToLower().Contains(loweredSearch)));
        }

        var matchingCount = await entries.CountAsync(cancellationToken);

        var items = await entries
            .OrderByDescending(entry => entry.Mapping.Id)
            .Skip(Math.Max(query.Skip, 0))
            .Take(Math.Clamp(query.Take, 1, MaximumPageSize))
            .Select(entry => new StreamTmdbMappingEntry(
                entry.Mapping.Id,
                entry.Mapping.ContentType,
                entry.Mapping.StreamId,
                entry.Mapping.TmdbId,
                entry.Mapping.LookupAttemptCount,
                entry.Mapping.NextLookupAtUtc,
                entry.Info == null ? null : entry.Info.Title,
                entry.Info == null ? null : entry.Info.OriginalTitle,
                entry.Info == null ? null : entry.Info.ReleaseDate,
                entry.Info == null ? null : entry.Info.PosterPath))
            .ToListAsync(cancellationToken);

        return new StreamTmdbMappingPage(items, matchingCount, totalCount, mappedCount);
    }

    /// <summary>
    /// Sets the TMDB ID of a mapping, found or not by the background lookup, which then never looks it up again;
    /// the TMDB metadata of the new ID is enqueued for loading. Returns false when the mapping does not exist.
    /// </summary>
    public async Task<bool> SetTmdbIdAsync(int id, long tmdbId, CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(tmdbId);

        var mapping = await dbContext.StreamTmdbMappings
            .FirstOrDefaultAsync(mapping => mapping.Id == id, cancellationToken);

        if (mapping is null)
        {
            return false;
        }

        mapping.TmdbId = tmdbId;
        mapping.NextLookupAtUtc = null;
        mapping.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        // the metadata loader ignores the request when the metadata is already loaded and not due for a refresh
        tmdbInfoQueue.TryEnqueue(new TmdbInfoRequest(mapping.ContentType, tmdbId));

        return true;
    }
}
