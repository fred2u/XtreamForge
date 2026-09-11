using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using XtreamForge.Infrastructure.Data;
using XtreamForge.Infrastructure.Models;

namespace XtreamForge.Infrastructure.Services;

public sealed class XtreamCategoryMappingService(
    IDbContextFactory<XtreamForgeDbContext> dbContextFactory,
    CategoryRuleEvaluator ruleEvaluator)
{
    private const int MaxSyncAttempts = 3;

    public async Task<IReadOnlyList<RewrittenCategory>> SyncCategoriesAsync(
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
                await SynchronizeUpstreamCategoriesAsync(dbContext, source.Id, contentType, normalizedCategories, discoveredAt, cancellationToken);
                source.LastSeenAtUtc = discoveredAt;
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                var effectiveCategories = await GetEffectiveOutputCategoriesAsync(dbContext, source.Id, contentType, normalizedCategories.Select(category => category.UpstreamCategoryId).ToHashSet(StringComparer.Ordinal), cancellationToken);
                return effectiveCategories
                    .Select(category => new RewrittenCategory(category.XtreamForgeCategoryId.ToString(), category.DisplayName, category.IncludedUpstreamCategoryIds))
                    .ToList();
            }
            catch (DbUpdateException exception) when (attempt < MaxSyncAttempts && IsUniqueConstraintViolation(exception))
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
        }

        throw new InvalidOperationException("Unable to synchronize Xtream categories after multiple attempts.");
    }

    public async Task<CategoryAdministrationView> GetAdministrationViewAsync(
        int? selectedSourceId,
        ContentType selectedContentType,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var sources = await dbContext.XtreamSources
            .OrderBy(source => source.Host)
            .ThenBy(source => source.Port)
            .Select(source => new XtreamSourceSummary(source.Id, source.Protocol, source.Host, source.Port, source.LastSeenAtUtc))
            .ToListAsync(cancellationToken);

        var effectiveSourceId = selectedSourceId ?? sources.FirstOrDefault()?.Id;
        if (effectiveSourceId is null)
        {
            return new CategoryAdministrationView(sources, null, selectedContentType, [], []);
        }

        var rules = await LoadRuleDefinitionsAsync(dbContext, effectiveSourceId.Value, selectedContentType, cancellationToken);

        var outputCategories = await dbContext.OutputCategories
            .Where(outputCategory => outputCategory.XtreamSourceId == effectiveSourceId.Value && outputCategory.ContentType == selectedContentType)
            .OrderBy(outputCategory => outputCategory.SortOrder)
            .ThenBy(outputCategory => outputCategory.XtreamForgeCategoryId)
            .Select(outputCategory => new OutputCategorySummary(
                outputCategory.Id,
                outputCategory.XtreamForgeCategoryId,
                outputCategory.DisplayName,
                outputCategory.IsNameCustomized,
                outputCategory.IsEnabled))
            .ToListAsync(cancellationToken);

        var upstreamCategoryData = await dbContext.UpstreamCategories
            .Where(upstreamCategory => upstreamCategory.XtreamSourceId == effectiveSourceId.Value && upstreamCategory.ContentType == selectedContentType)
            .Include(upstreamCategory => upstreamCategory.OutputCategory)
            .Include(upstreamCategory => upstreamCategory.DedicatedOutputCategory)
            .OrderBy(upstreamCategory => upstreamCategory.UpstreamCategoryName)
            .Select(upstreamCategory => new
            {
                upstreamCategory.Id,
                upstreamCategory.UpstreamCategoryId,
                upstreamCategory.UpstreamCategoryName,
                upstreamCategory.IsExcluded,
                upstreamCategory.OutputCategoryId,
                upstreamCategory.DedicatedOutputCategoryId,
                ActiveXtreamForgeCategoryId = upstreamCategory.OutputCategory == null ? (int?)null : upstreamCategory.OutputCategory.XtreamForgeCategoryId,
                ActiveOutputName = upstreamCategory.OutputCategory == null ? null : upstreamCategory.OutputCategory.DisplayName,
                DedicatedXtreamForgeCategoryId = upstreamCategory.DedicatedOutputCategory.XtreamForgeCategoryId,
                DedicatedOutputName = upstreamCategory.DedicatedOutputCategory.DisplayName
            })
            .ToListAsync(cancellationToken);

        var upstreamCategories = upstreamCategoryData
            .Select(category =>
            {
                var evaluation = EvaluateEffectiveInclusion(category.IsExcluded, category.UpstreamCategoryName, rules);

                return new UpstreamCategorySummary(
                    category.Id,
                    category.UpstreamCategoryId,
                    category.UpstreamCategoryName,
                    category.IsExcluded,
                    category.OutputCategoryId,
                    category.DedicatedOutputCategoryId,
                    category.ActiveXtreamForgeCategoryId,
                    category.ActiveOutputName,
                    category.DedicatedXtreamForgeCategoryId,
                    category.DedicatedOutputName,
                    evaluation.Decision,
                    evaluation.MatchedRuleId,
                    evaluation.MatchedRuleSequence,
                    evaluation.MatchedRuleAction,
                    evaluation.MatchedRuleOperator,
                    evaluation.MatchedPattern,
                    evaluation.MatchedRuleCaseSensitive,
                    evaluation.Decision == CategoryInclusionDecision.Include);
            })
            .ToList();

        return new CategoryAdministrationView(sources, effectiveSourceId, selectedContentType, outputCategories, upstreamCategories);
    }

    public async Task<CategoryConfigurationResult> SaveCategoryConfigurationAsync(CategoryConfigurationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var upstreamCategory = await dbContext.UpstreamCategories
            .Include(category => category.OutputCategory)
            .Include(category => category.DedicatedOutputCategory)
            .SingleOrDefaultAsync(
                category => category.Id == command.UpstreamCategoryRecordId
                    && category.XtreamSourceId == command.SelectedSourceId
                    && category.ContentType == command.SelectedContentType,
                cancellationToken);

        if (upstreamCategory is null)
        {
            throw new InvalidOperationException("The category does not belong to the selected source or content type.");
        }

        var outputCategories = await dbContext.OutputCategories
            .Where(outputCategory => outputCategory.XtreamSourceId == upstreamCategory.XtreamSourceId && outputCategory.ContentType == upstreamCategory.ContentType)
            .ToListAsync(cancellationToken);

        var updatedAt = DateTimeOffset.UtcNow;
        if (command.IsExcluded)
        {
            upstreamCategory.IsExcluded = true;
            upstreamCategory.OutputCategory = null;
            upstreamCategory.OutputCategoryId = null;
            await dbContext.SaveChangesAsync(cancellationToken);
            return new CategoryConfigurationResult(upstreamCategory.XtreamSourceId, upstreamCategory.ContentType);
        }

        upstreamCategory.IsExcluded = false;

        var targetOutputCategory = command.MergeToOutputCategoryRecordId is int mergeTargetId
            ? outputCategories.Single(outputCategory => outputCategory.Id == mergeTargetId)
            : upstreamCategory.DedicatedOutputCategory;

        var desiredOutputName = string.IsNullOrWhiteSpace(command.OutputName)
            ? (targetOutputCategory.Id == upstreamCategory.DedicatedOutputCategoryId
                ? upstreamCategory.UpstreamCategoryName
                : targetOutputCategory.DisplayName)
            : command.OutputName.Trim();

        var isNameCustomized = targetOutputCategory.Id == upstreamCategory.DedicatedOutputCategoryId
            ? !desiredOutputName.Equals(upstreamCategory.UpstreamCategoryName, StringComparison.Ordinal)
            : targetOutputCategory.IsNameCustomized || !desiredOutputName.Equals(targetOutputCategory.DisplayName, StringComparison.Ordinal);

        targetOutputCategory.DisplayName = desiredOutputName;
        targetOutputCategory.IsNameCustomized = isNameCustomized;
        targetOutputCategory.IsEnabled = true;
        targetOutputCategory.UpdatedAtUtc = updatedAt;

        upstreamCategory.OutputCategory = targetOutputCategory;
        upstreamCategory.OutputCategoryId = targetOutputCategory.Id;

        if (upstreamCategory.DedicatedOutputCategoryId == targetOutputCategory.Id && !targetOutputCategory.IsNameCustomized)
        {
            targetOutputCategory.DisplayName = upstreamCategory.UpstreamCategoryName;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new CategoryConfigurationResult(upstreamCategory.XtreamSourceId, upstreamCategory.ContentType);
    }

    public async Task<IReadOnlyList<EffectiveOutputCategoryMapping>> GetEffectiveOutputCategoryMappingsAsync(
        int sourceId,
        ContentType contentType,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await GetEffectiveOutputCategoriesAsync(dbContext, sourceId, contentType, null, cancellationToken);
    }

    public async Task<IReadOnlyList<EffectiveOutputCategoryMapping>> GetEffectiveOutputCategoryMappingsAsync(
        XtreamSourceDescriptor sourceDescriptor,
        ContentType contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceDescriptor);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var sourceId = await dbContext.XtreamSources
            .Where(source => source.Protocol == sourceDescriptor.Protocol
                && source.Host == sourceDescriptor.Host
                && source.Port == sourceDescriptor.Port)
            .Select(source => (int?)source.Id)
            .SingleOrDefaultAsync(cancellationToken);

        return sourceId is int resolvedSourceId
            ? await GetEffectiveOutputCategoriesAsync(dbContext, resolvedSourceId, contentType, null, cancellationToken)
            : [];
    }

    public async Task<bool> HasDiscoveredCategoriesAsync(
        XtreamSourceDescriptor sourceDescriptor,
        ContentType contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceDescriptor);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await dbContext.UpstreamCategories
            .AnyAsync(category => category.XtreamSource.Protocol == sourceDescriptor.Protocol
                && category.XtreamSource.Host == sourceDescriptor.Host
                && category.XtreamSource.Port == sourceDescriptor.Port
                && category.ContentType == contentType,
                cancellationToken);
    }

    private static List<DiscoveredCategory> NormalizeDiscoveredCategories(IReadOnlyList<DiscoveredCategory> discoveredCategories) =>
        discoveredCategories
            .Where(category => !string.IsNullOrWhiteSpace(category.UpstreamCategoryId) && !string.IsNullOrWhiteSpace(category.UpstreamCategoryName))
            .Select(category => new DiscoveredCategory(category.UpstreamCategoryId.Trim(), category.UpstreamCategoryName.Trim()))
            .ToList();

    private async Task SynchronizeUpstreamCategoriesAsync(
        XtreamForgeDbContext dbContext,
        int sourceId,
        ContentType contentType,
        IReadOnlyList<DiscoveredCategory> normalizedCategories,
        DateTimeOffset discoveredAt,
        CancellationToken cancellationToken)
    {
        var outputCategories = await dbContext.OutputCategories
            .Where(outputCategory => outputCategory.XtreamSourceId == sourceId && outputCategory.ContentType == contentType)
            .ToListAsync(cancellationToken);

        var upstreamCategories = await dbContext.UpstreamCategories
            .Where(upstreamCategory => upstreamCategory.XtreamSourceId == sourceId && upstreamCategory.ContentType == contentType)
            .Include(upstreamCategory => upstreamCategory.OutputCategory)
            .Include(upstreamCategory => upstreamCategory.DedicatedOutputCategory)
            .ToDictionaryAsync(upstreamCategory => upstreamCategory.UpstreamCategoryId, StringComparer.Ordinal, cancellationToken);

        var nextOutputId = outputCategories.Count == 0
            ? 1
            : outputCategories.Max(outputCategory => outputCategory.XtreamForgeCategoryId) + 1;
        var nextSortOrder = outputCategories.Count == 0
            ? 1
            : outputCategories.Max(outputCategory => outputCategory.SortOrder) + 1;

        foreach (var discoveredCategory in normalizedCategories)
        {
            if (!upstreamCategories.TryGetValue(discoveredCategory.UpstreamCategoryId, out var upstreamCategory))
            {
                var outputCategory = CreateOutputCategory(sourceId, contentType, nextOutputId++, nextSortOrder++, discoveredCategory.UpstreamCategoryName, false, discoveredAt);
                dbContext.OutputCategories.Add(outputCategory);

                upstreamCategory = new UpstreamCategory
                {
                    XtreamSourceId = sourceId,
                    ContentType = contentType,
                    UpstreamCategoryId = discoveredCategory.UpstreamCategoryId,
                    UpstreamCategoryName = discoveredCategory.UpstreamCategoryName,
                    OutputCategory = outputCategory,
                    DedicatedOutputCategory = outputCategory,
                    IsExcluded = false,
                    FirstDiscoveredAtUtc = discoveredAt,
                    LastDiscoveredAtUtc = discoveredAt
                };

                dbContext.UpstreamCategories.Add(upstreamCategory);
                upstreamCategories.Add(upstreamCategory.UpstreamCategoryId, upstreamCategory);
                outputCategories.Add(outputCategory);
                continue;
            }

            upstreamCategory.UpstreamCategoryName = discoveredCategory.UpstreamCategoryName;
            upstreamCategory.LastDiscoveredAtUtc = discoveredAt;

            if (upstreamCategory.DedicatedOutputCategory is null)
            {
                var dedicatedOutputCategory = CreateOutputCategory(sourceId, contentType, nextOutputId++, nextSortOrder++, discoveredCategory.UpstreamCategoryName, false, discoveredAt);
                dbContext.OutputCategories.Add(dedicatedOutputCategory);
                upstreamCategory.DedicatedOutputCategory = dedicatedOutputCategory;
                upstreamCategory.OutputCategory ??= dedicatedOutputCategory;
                outputCategories.Add(dedicatedOutputCategory);
            }

            if (!upstreamCategory.IsExcluded && upstreamCategory.OutputCategoryId is null)
            {
                upstreamCategory.OutputCategory = upstreamCategory.DedicatedOutputCategory;
            }

            if (upstreamCategory.OutputCategoryId == upstreamCategory.DedicatedOutputCategoryId
                && !upstreamCategory.DedicatedOutputCategory.IsNameCustomized)
            {
                upstreamCategory.DedicatedOutputCategory.DisplayName = discoveredCategory.UpstreamCategoryName;
                upstreamCategory.DedicatedOutputCategory.UpdatedAtUtc = discoveredAt;
            }
        }
    }

    private async Task<IReadOnlyList<EffectiveOutputCategoryMapping>> GetEffectiveOutputCategoriesAsync(
        XtreamForgeDbContext dbContext,
        int sourceId,
        ContentType contentType,
        ISet<string>? restrictToUpstreamCategoryIds,
        CancellationToken cancellationToken)
    {
        var rules = await LoadRuleDefinitionsAsync(dbContext, sourceId, contentType, cancellationToken);

        var upstreamCategories = await dbContext.UpstreamCategories
            .Where(upstreamCategory => upstreamCategory.XtreamSourceId == sourceId && upstreamCategory.ContentType == contentType)
            .Include(upstreamCategory => upstreamCategory.OutputCategory)
            .Where(upstreamCategory => upstreamCategory.OutputCategoryId != null)
            .OrderBy(upstreamCategory => upstreamCategory.OutputCategory!.SortOrder)
            .ThenBy(upstreamCategory => upstreamCategory.OutputCategory!.XtreamForgeCategoryId)
            .ThenBy(upstreamCategory => upstreamCategory.UpstreamCategoryName)
            .ToListAsync(cancellationToken);

        var includedUpstreamCategories = upstreamCategories
            .Where(upstreamCategory => restrictToUpstreamCategoryIds is null || restrictToUpstreamCategoryIds.Contains(upstreamCategory.UpstreamCategoryId))
            .Select(upstreamCategory => new
            {
                Category = upstreamCategory,
                Evaluation = EvaluateEffectiveInclusion(upstreamCategory.IsExcluded, upstreamCategory.UpstreamCategoryName, rules)
            })
            .Where(result => result.Evaluation.Decision == CategoryInclusionDecision.Include && result.Category.OutputCategory is not null)
            .ToList();

        return includedUpstreamCategories
            .GroupBy(result => result.Category.OutputCategory!.Id)
            .Select(group =>
            {
                var outputCategory = group.First().Category.OutputCategory!;
                return new EffectiveOutputCategoryMapping(
                    outputCategory.Id,
                    outputCategory.XtreamForgeCategoryId,
                    outputCategory.SortOrder,
                    outputCategory.DisplayName,
                    group.Select(result => result.Category.UpstreamCategoryId).Distinct(StringComparer.Ordinal).ToList());
            })
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.XtreamForgeCategoryId)
            .ToList();
    }

    private CategoryRuleEvaluationResult EvaluateEffectiveInclusion(
        bool isManuallyExcluded,
        string categoryName,
        IReadOnlyList<CategoryRuleDefinition> rules)
    {
        if (isManuallyExcluded)
        {
            return new CategoryRuleEvaluationResult(
                CategoryInclusionDecision.Exclude,
                null,
                null,
                null,
                null,
                null,
                null);
        }

        return ruleEvaluator.Evaluate(categoryName, rules);
    }

    private static OutputCategory CreateOutputCategory(
        int sourceId,
        ContentType contentType,
        int xtreamForgeCategoryId,
        int sortOrder,
        string displayName,
        bool isNameCustomized,
        DateTimeOffset createdAtUtc) =>
        new()
        {
            XtreamSourceId = sourceId,
            ContentType = contentType,
            XtreamForgeCategoryId = xtreamForgeCategoryId,
            DisplayName = displayName,
            SortOrder = sortOrder,
            IsNameCustomized = isNameCustomized,
            CreatedAtUtc = createdAtUtc,
            UpdatedAtUtc = createdAtUtc
        };

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
        await dbContext.SaveChangesAsync(cancellationToken);
        return source;
    }

    private static async Task<IReadOnlyList<CategoryRuleDefinition>> LoadRuleDefinitionsAsync(
        XtreamForgeDbContext dbContext,
        int sourceId,
        ContentType contentType,
        CancellationToken cancellationToken)
    {
        return await dbContext.CategoryRules
            .Where(rule => rule.XtreamSourceId == sourceId && rule.ContentType == contentType)
            .OrderBy(rule => rule.Sequence)
            .ThenBy(rule => rule.Id)
            .Select(rule => new CategoryRuleDefinition(
                rule.Id,
                rule.Sequence,
                rule.Action,
                rule.Operator,
                rule.Pattern,
                rule.CaseSensitive,
                rule.IsEnabled))
            .ToListAsync(cancellationToken);
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }
        or SqliteException { SqliteExtendedErrorCode: 2067 }
        or SqliteException { SqliteErrorCode: 19 };
}

public sealed record XtreamSourceDescriptor(string Protocol, string Host, int Port);

public sealed record DiscoveredCategory(string UpstreamCategoryId, string UpstreamCategoryName);

public sealed record RewrittenCategory(string CategoryId, string CategoryName, IReadOnlyList<string> IncludedUpstreamCategoryIds);

public sealed record EffectiveOutputCategoryMapping(
    int OutputCategoryRecordId,
    int XtreamForgeCategoryId,
    int SortOrder,
    string DisplayName,
    IReadOnlyList<string> IncludedUpstreamCategoryIds);

public sealed record XtreamSourceSummary(int Id, string Protocol, string Host, int Port, DateTimeOffset LastSeenAtUtc);

public sealed record OutputCategorySummary(int Id, int XtreamForgeCategoryId, string DisplayName, bool IsNameCustomized, bool IsEnabled);

public sealed record UpstreamCategorySummary(
    int Id,
    string UpstreamCategoryId,
    string UpstreamCategoryName,
    bool IsManuallyExcluded,
    int? OutputCategoryId,
    int DedicatedOutputCategoryId,
    int? ActiveXtreamForgeCategoryId,
    string? ActiveOutputName,
    int DedicatedXtreamForgeCategoryId,
    string DedicatedOutputName,
    CategoryInclusionDecision EffectiveDecision,
    int? MatchedRuleId,
    int? MatchedRuleSequence,
    CategoryRuleAction? MatchedRuleAction,
    CategoryRuleOperator? MatchedRuleOperator,
    string? MatchedPattern,
    bool? MatchedRuleCaseSensitive,
    bool IsEffectivelyIncluded);

public sealed record CategoryAdministrationView(
    IReadOnlyList<XtreamSourceSummary> Sources,
    int? SelectedSourceId,
    ContentType SelectedContentType,
    IReadOnlyList<OutputCategorySummary> OutputCategories,
    IReadOnlyList<UpstreamCategorySummary> UpstreamCategories);

public sealed record CategoryConfigurationCommand(
    int UpstreamCategoryRecordId,
    int SelectedSourceId,
    ContentType SelectedContentType,
    bool IsExcluded,
    int? MergeToOutputCategoryRecordId,
    string? OutputName);

public sealed record CategoryConfigurationResult(
    int SourceId,
    ContentType ContentType);
