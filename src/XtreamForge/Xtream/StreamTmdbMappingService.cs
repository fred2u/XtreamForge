using Microsoft.EntityFrameworkCore;
using XtreamForge.Categories;
using XtreamForge.Data;

namespace XtreamForge.Xtream;

public sealed class StreamTmdbMappingService(IDbContextFactory<XtreamForgeDbContext> dbContextFactory)
{
    public async Task<StreamTmdbMappingSet> GetMappingsAsync(
        int sourceId,
        ContentType contentType,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var mappings = await dbContext.StreamTmdbMappings
            .Where(mapping => mapping.XtreamSourceId == sourceId && mapping.ContentType == contentType)
            .ToDictionaryAsync(mapping => mapping.StreamId, mapping => mapping.TmdbId, StringComparer.Ordinal, cancellationToken);

        return new StreamTmdbMappingSet(sourceId, mappings);
    }

    public Task UpsertMappingAsync(
        int sourceId,
        ContentType contentType,
        string streamId,
        long tmdbId,
        CancellationToken cancellationToken = default) =>
        UpsertMappingsAsync(sourceId, contentType, new Dictionary<string, long>(StringComparer.Ordinal)
        {
            [streamId] = tmdbId
        }, cancellationToken);

    public async Task UpsertMappingsAsync(
        int sourceId,
        ContentType contentType,
        IReadOnlyDictionary<string, long> mappings,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(sourceId);
        ArgumentNullException.ThrowIfNull(mappings);

        var normalizedMappings = mappings
            .Where(static pair => !string.IsNullOrWhiteSpace(pair.Key) && pair.Value > 0)
            .ToDictionary(pair => pair.Key.Trim(), pair => pair.Value, StringComparer.Ordinal);

        if (normalizedMappings.Count == 0)
        {
            return;
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var timestamp = DateTimeOffset.UtcNow;
        var streamIds = normalizedMappings.Keys.ToList();

        var existingMappings = await dbContext.StreamTmdbMappings
            .Where(mapping => mapping.XtreamSourceId == sourceId
                && mapping.ContentType == contentType
                && streamIds.Contains(mapping.StreamId))
            .ToDictionaryAsync(mapping => mapping.StreamId, StringComparer.Ordinal, cancellationToken);

        foreach (var (streamId, tmdbId) in normalizedMappings)
        {
            if (existingMappings.TryGetValue(streamId, out var existingMapping))
            {
                if (existingMapping.TmdbId != tmdbId)
                {
                    existingMapping.TmdbId = tmdbId;
                    existingMapping.UpdatedAtUtc = timestamp;
                }

                continue;
            }

            dbContext.StreamTmdbMappings.Add(new StreamTmdbMapping
            {
                XtreamSourceId = sourceId,
                ContentType = contentType,
                StreamId = streamId,
                TmdbId = tmdbId,
                CreatedAtUtc = timestamp,
                UpdatedAtUtc = timestamp
            });
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

public sealed record StreamTmdbMappingSet(
    int? SourceId,
    IReadOnlyDictionary<string, long> Mappings);
