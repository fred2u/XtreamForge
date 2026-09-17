using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using XtreamForge.Categories;
using XtreamForge.Data;

namespace XtreamForge.Source;

public sealed class SourceService(IDbContextFactory<XtreamForgeDbContext> dbContextFactory)
{
    private const int MaxSyncAttempts = 3;

    public async Task<IReadOnlyList<XtreamSourceSummary>> GetSourcesAsync(CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.XtreamSources
            .AsNoTracking()
            .OrderBy(source => source.Host)
            .ThenBy(source => source.Port)
            .Select(source => new XtreamSourceSummary(source.Id, source.Protocol, source.Host, source.Port, source.LastSeenAtUtc))
            .ToListAsync(cancellationToken);
    }

    public async Task<int?> GetSourceIdAsync(XtreamSourceDescriptor sourceDescriptor, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceDescriptor);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.XtreamSources
            .AsNoTracking()
            .Where(source => source.Protocol == sourceDescriptor.Protocol
                && source.Host == sourceDescriptor.Host
                && source.Port == sourceDescriptor.Port)
            .Select(source => (int?)source.Id)
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<bool> SourceExistsAsync(int sourceId, CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.XtreamSources
            .AsNoTracking()
            .AnyAsync(source => source.Id == sourceId, cancellationToken);
    }

    private static async Task<XtreamSource> GetOrCreateSourceAsync(
        XtreamForgeDbContext dbContext,
        XtreamSourceDescriptor sourceDescriptor,
        DateTimeOffset discoveredAt,
        CancellationToken cancellationToken)
    {
        var source = await dbContext.XtreamSources.SingleOrDefaultAsync(
            existingSource => existingSource.Protocol == sourceDescriptor.Protocol
                && existingSource.Host == sourceDescriptor.Host
                && existingSource.Port == sourceDescriptor.Port,
            cancellationToken);

        if (source is not null)
        {
            source.LastSeenAtUtc = discoveredAt;
            return source;
        }

        source = new XtreamSource
        {
            Protocol = sourceDescriptor.Protocol,
            Host = sourceDescriptor.Host,
            Port = sourceDescriptor.Port,
            FirstSeenAtUtc = discoveredAt,
            LastSeenAtUtc = discoveredAt
        };

        dbContext.XtreamSources.Add(source);
        return source;
    }

    public async Task<bool> HasDiscoveredCategoriesAsync(
        XtreamSourceDescriptor sourceDescriptor,
        ContentType contentType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sourceDescriptor);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.UpstreamCategories
            .AsNoTracking()
            .AnyAsync(category => category.XtreamSource.Protocol == sourceDescriptor.Protocol
                && category.XtreamSource.Host == sourceDescriptor.Host
                && category.XtreamSource.Port == sourceDescriptor.Port
                && category.ContentType == contentType,
                cancellationToken);
    }

    public async Task<SourceCategorySynchronizationResult> SynchronizeCategoriesAsync(
        XtreamSourceDescriptor sourceDescriptor,
        ContentType contentType,
        IReadOnlyList<DiscoveredCategory> discoveredCategories,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceDescriptor);
        ArgumentNullException.ThrowIfNull(discoveredCategories);

        var normalizedCategories = NormalizeDiscoveredCategories(discoveredCategories);

        for (var attempt = 1; attempt <= MaxSyncAttempts; attempt++)
        {
            var discoveredAt = DateTimeOffset.UtcNow;

            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                var source = await GetOrCreateSourceAsync(dbContext, sourceDescriptor, discoveredAt, cancellationToken);

                var synchronizedBatch = await SynchronizeUpstreamCategoriesAsync(
                    dbContext,
                    source,
                    contentType,
                    normalizedCategories,
                    discoveredAt,
                    cancellationToken);

                source.LastSeenAtUtc = discoveredAt;

                if (dbContext.ChangeTracker.HasChanges())
                {
                    await dbContext.SaveChangesAsync(cancellationToken);
                }

                if (synchronizedBatch.ExistingCategoryIdsToTouch.Count > 0)
                {
                    await TouchDiscoveredCategoriesAsync(
                        dbContext,
                        source.Id,
                        contentType,
                        discoveredAt,
                        synchronizedBatch,
                        cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);

                return new SourceCategorySynchronizationResult(
                    source.Id,
                    [.. synchronizedBatch.RequestCategories.Select(category => CreateSourceCategorySnapshot(category, discoveredAt))],
                    synchronizedBatch.CreatedCategoryCount,
                    synchronizedBatch.UpdatedCategoryCount);
            }
            catch (DbUpdateException exception) when (attempt < MaxSyncAttempts && IsUniqueConstraintViolation(exception))
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
        }

        throw new InvalidOperationException("Unable to synchronize Xtream categories after multiple attempts.");
    }

    public async Task<IReadOnlyList<SourceCategorySnapshot>> GetSourceCategoriesAsync(
        int sourceId,
        ContentType contentType,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var query = dbContext.UpstreamCategories
            .AsNoTracking()
            .Where(category => category.XtreamSourceId == sourceId && category.ContentType == contentType);

        return await query
            .OrderBy(category => category.DedicatedOutputCategory.SortOrder)
            .ThenBy(category => category.UpstreamCategoryName)
            .Select(category => new SourceCategorySnapshot(
                category.Id,
                category.XtreamSourceId,
                category.ContentType,
                category.UpstreamCategoryId,
                category.UpstreamCategoryName,
                category.IsExcluded,
                category.DedicatedOutputCategoryId,
                category.DedicatedOutputCategory.XtreamForgeCategoryId,
                category.DedicatedOutputCategory.DisplayName,
                category.DedicatedOutputCategory.SortOrder,
                category.CustomCategoryId,
                category.CustomCategory != null ? category.CustomCategory.XtreamForgeCategoryId : null,
                category.CustomCategory != null ? category.CustomCategory.DisplayName : null,
                category.FirstDiscoveredAtUtc,
                category.LastDiscoveredAtUtc))
            .ToListAsync(cancellationToken);
    }

    private static List<DiscoveredCategory> NormalizeDiscoveredCategories(IReadOnlyList<DiscoveredCategory> discoveredCategories)
    {
        var normalizedCategories = new List<DiscoveredCategory>(discoveredCategories.Count);
        var categoryIndexById = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var category in discoveredCategories)
        {
            if (string.IsNullOrWhiteSpace(category.UpstreamCategoryId) || string.IsNullOrWhiteSpace(category.UpstreamCategoryName))
            {
                continue;
            }

            var normalizedCategory = new DiscoveredCategory(category.UpstreamCategoryId.Trim(), category.UpstreamCategoryName.Trim());
            if (categoryIndexById.TryGetValue(normalizedCategory.UpstreamCategoryId, out var existingIndex))
            {
                normalizedCategories[existingIndex] = normalizedCategory;
                continue;
            }

            categoryIndexById[normalizedCategory.UpstreamCategoryId] = normalizedCategories.Count;
            normalizedCategories.Add(normalizedCategory);
        }

        return normalizedCategories;
    }

    private static async Task<SynchronizedCategoryBatch> SynchronizeUpstreamCategoriesAsync(
        XtreamForgeDbContext dbContext,
        XtreamSource source,
        ContentType contentType,
        List<DiscoveredCategory> normalizedCategories,
        DateTimeOffset discoveredAt,
        CancellationToken cancellationToken)
    {
        var upstreamCategories = source.Id == 0
            ? new Dictionary<string, UpstreamCategory>(StringComparer.Ordinal)
            : await dbContext.UpstreamCategories
                .Where(category => category.XtreamSourceId == source.Id && category.ContentType == contentType)
                .Include(category => category.DedicatedOutputCategory)
                .Include(category => category.CustomCategory)
                .ToDictionaryAsync(category => category.UpstreamCategoryId, StringComparer.Ordinal, cancellationToken);

        var requestCategories = new List<UpstreamCategory>(normalizedCategories.Count);
        var newOutputCategories = new List<OutputCategory>();
        var newUpstreamCategories = new List<UpstreamCategory>();
        var existingCategoryIdsToTouch = new List<int>(normalizedCategories.Count);
        var existingCategoryCount = upstreamCategories.Count;
        var nextSortOrder = upstreamCategories.Values
            .Where(category => category.DedicatedOutputCategory is not null)
            .Select(category => category.DedicatedOutputCategory!.SortOrder)
            .DefaultIfEmpty()
            .Max();
        int? nextXtreamForgeCategoryId = null;
        var updatedCategoryCount = 0;

        foreach (var discoveredCategory in normalizedCategories)
        {
            if (!upstreamCategories.TryGetValue(discoveredCategory.UpstreamCategoryId, out var upstreamCategory))
            {
                nextXtreamForgeCategoryId ??= await GetNextXtreamForgeCategoryIdAsync(dbContext, contentType, cancellationToken);
                var allocatedCategoryId = nextXtreamForgeCategoryId.Value;
                nextXtreamForgeCategoryId = allocatedCategoryId + 1;
                var outputCategory = CreateOutputCategory(source, contentType, allocatedCategoryId, ++nextSortOrder, discoveredCategory.UpstreamCategoryName, discoveredAt);
                newOutputCategories.Add(outputCategory);

                upstreamCategory = new UpstreamCategory
                {
                    XtreamSource = source,
                    ContentType = contentType,
                    UpstreamCategoryId = discoveredCategory.UpstreamCategoryId,
                    UpstreamCategoryName = discoveredCategory.UpstreamCategoryName,
                    DedicatedOutputCategory = outputCategory,
                    IsExcluded = false,
                    FirstDiscoveredAtUtc = discoveredAt,
                    LastDiscoveredAtUtc = discoveredAt
                };

                newUpstreamCategories.Add(upstreamCategory);
                upstreamCategories.Add(upstreamCategory.UpstreamCategoryId, upstreamCategory);
                requestCategories.Add(upstreamCategory);
                continue;
            }

            requestCategories.Add(upstreamCategory);
            existingCategoryIdsToTouch.Add(upstreamCategory.Id);
            var categoryUpdated = false;

            if (!string.Equals(upstreamCategory.UpstreamCategoryName, discoveredCategory.UpstreamCategoryName, StringComparison.Ordinal))
            {
                upstreamCategory.UpstreamCategoryName = discoveredCategory.UpstreamCategoryName;
                categoryUpdated = true;
            }

            if (upstreamCategory.DedicatedOutputCategory is null)
            {
                nextXtreamForgeCategoryId ??= await GetNextXtreamForgeCategoryIdAsync(dbContext, contentType, cancellationToken);
                var allocatedCategoryId = nextXtreamForgeCategoryId.Value;
                nextXtreamForgeCategoryId = allocatedCategoryId + 1;
                var outputCategory = CreateOutputCategory(source, contentType, allocatedCategoryId, ++nextSortOrder, discoveredCategory.UpstreamCategoryName, discoveredAt);
                newOutputCategories.Add(outputCategory);
                upstreamCategory.DedicatedOutputCategory = outputCategory;
                categoryUpdated = true;
            }
            else if (!string.Equals(upstreamCategory.DedicatedOutputCategory.DisplayName, discoveredCategory.UpstreamCategoryName, StringComparison.Ordinal))
            {
                upstreamCategory.DedicatedOutputCategory.DisplayName = discoveredCategory.UpstreamCategoryName;
                upstreamCategory.DedicatedOutputCategory.UpdatedAtUtc = discoveredAt;
                categoryUpdated = true;
            }

            if (categoryUpdated)
            {
                updatedCategoryCount++;
            }
        }

        if (newOutputCategories.Count > 0)
        {
            dbContext.OutputCategories.AddRange(newOutputCategories);
        }

        if (newUpstreamCategories.Count > 0)
        {
            dbContext.UpstreamCategories.AddRange(newUpstreamCategories);
        }

        return new SynchronizedCategoryBatch(
            requestCategories,
            newUpstreamCategories.Count,
            updatedCategoryCount,
            existingCategoryIdsToTouch,
            newUpstreamCategories.Count == 0 && existingCategoryCount > 0 && existingCategoryIdsToTouch.Count == existingCategoryCount);
    }

    private static OutputCategory CreateOutputCategory(
        XtreamSource source,
        ContentType contentType,
        int xtreamForgeCategoryId,
        int sortOrder,
        string displayName,
        DateTimeOffset createdAtUtc) =>
        new()
        {
            XtreamSource = source,
            XtreamSourceId = source.Id,
            ContentType = contentType,
            XtreamForgeCategoryId = xtreamForgeCategoryId,
            DisplayName = displayName,
            SortOrder = sortOrder,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
        };

    private static async Task TouchDiscoveredCategoriesAsync(
        XtreamForgeDbContext dbContext,
        int sourceId,
        ContentType contentType,
        DateTimeOffset discoveredAt,
        SynchronizedCategoryBatch synchronizedBatch,
        CancellationToken cancellationToken)
    {
        if (synchronizedBatch.TouchAllExistingCategories)
        {
            await dbContext.UpstreamCategories
                .Where(category => category.XtreamSourceId == sourceId && category.ContentType == contentType)
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(category => category.LastDiscoveredAtUtc, discoveredAt),
                    cancellationToken);
            return;
        }

        foreach (var categoryIdBatch in synchronizedBatch.ExistingCategoryIdsToTouch.Chunk(500))
        {
            await dbContext.UpstreamCategories
                .Where(category => categoryIdBatch.Contains(category.Id))
                .ExecuteUpdateAsync(
                    setters => setters.SetProperty(category => category.LastDiscoveredAtUtc, discoveredAt),
                    cancellationToken);
        }
    }

    private static async Task<int> GetNextXtreamForgeCategoryIdAsync(
        XtreamForgeDbContext dbContext,
        ContentType contentType,
        CancellationToken cancellationToken)
    {
        var nextCategoryId = await dbContext.OutputCategories
            .Where(category => category.ContentType == contentType)
            .Select(category => (int?)category.XtreamForgeCategoryId)
            .Concat(
                dbContext.CustomCategories
                    .Where(category => category.ContentType == contentType)
                    .Select(category => (int?)category.XtreamForgeCategoryId))
            .MaxAsync(cancellationToken) ?? 0;

        return nextCategoryId + 1;
    }

    private static SourceCategorySnapshot CreateSourceCategorySnapshot(
        UpstreamCategory category,
        DateTimeOffset discoveredAt) =>
        new(
            category.Id,
            category.XtreamSourceId,
            category.ContentType,
            category.UpstreamCategoryId,
            category.UpstreamCategoryName,
            category.IsExcluded,
            category.DedicatedOutputCategoryId,
            category.DedicatedOutputCategory.XtreamForgeCategoryId,
            category.DedicatedOutputCategory.DisplayName,
            category.DedicatedOutputCategory.SortOrder,
            category.CustomCategoryId,
            category.CustomCategory?.XtreamForgeCategoryId,
            category.CustomCategory?.DisplayName,
            category.FirstDiscoveredAtUtc,
            discoveredAt);

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
        or SqliteException { SqliteExtendedErrorCode: 2067 }
        or SqliteException { SqliteErrorCode: 19 };

    private sealed record SynchronizedCategoryBatch(
        IReadOnlyList<UpstreamCategory> RequestCategories,
        int CreatedCategoryCount,
        int UpdatedCategoryCount,
        IReadOnlyList<int> ExistingCategoryIdsToTouch,
        bool TouchAllExistingCategories);
}

public sealed record XtreamSourceDescriptor(string Protocol, string Host, int Port);

public sealed record XtreamSourceSummary(int Id, string Protocol, string Host, int Port, DateTimeOffset LastSeenAtUtc);

public sealed record SourceCategorySynchronizationResult(
    int SourceId,
    IReadOnlyList<SourceCategorySnapshot> Categories,
    int CreatedCategoryCount,
    int UpdatedCategoryCount);

public sealed record SourceCategorySnapshot(
    int UpstreamCategoryRecordId,
    int SourceId,
    ContentType ContentType,
    string UpstreamCategoryId,
    string UpstreamCategoryName,
    bool IsExcluded,
    int DedicatedOutputCategoryRecordId,
    int DedicatedXtreamForgeCategoryId,
    string DedicatedOutputName,
    int DedicatedOutputSortOrder,
    int? CustomCategoryId,
    int? CustomXtreamForgeCategoryId,
    string? CustomCategoryName,
    DateTimeOffset FirstDiscoveredAtUtc,
    DateTimeOffset LastDiscoveredAtUtc);
