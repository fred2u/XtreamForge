using Microsoft.EntityFrameworkCore;
using XtreamForge.Categories;
using XtreamForge.Data;
using XtreamForge.Source;

namespace XtreamForge.Xtream;

public sealed class StreamTmdbMappingService(
    IDbContextFactory<XtreamForgeDbContext> dbContextFactory,
    SourceService sourceService)
{
    public async Task<StreamTmdbMappingSet> GetMappingsAsync(
        XtreamSourceDescriptor sourceDescriptor,
        ContentType contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceDescriptor);

        var sourceId = await sourceService.GetSourceIdAsync(sourceDescriptor, cancellationToken);

        if (sourceId is null)
        {
            return new StreamTmdbMappingSet(null, new Dictionary<string, long>(StringComparer.Ordinal));
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var mappings = await dbContext.StreamTmdbMappings
            .Where(mapping => mapping.XtreamSourceId == sourceId.Value && mapping.ContentType == contentType)
            .ToDictionaryAsync(mapping => mapping.StreamId, mapping => mapping.TmdbId, StringComparer.Ordinal, cancellationToken);

        return new StreamTmdbMappingSet(sourceId.Value, mappings);
    }

    public async Task<int?> GetSourceIdAsync(
        XtreamSourceDescriptor sourceDescriptor,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceDescriptor);
        return await sourceService.GetSourceIdAsync(sourceDescriptor, cancellationToken);
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
