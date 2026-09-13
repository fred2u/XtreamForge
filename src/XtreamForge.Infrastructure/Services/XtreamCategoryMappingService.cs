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
    public const int MaxCustomCategoryNameLength = 255;

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
                await SynchronizeUpstreamCategoriesAsync(dbContext, source, contentType, normalizedCategories, discoveredAt, cancellationToken);
                source.LastSeenAtUtc = discoveredAt;
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                var effectiveCategories = await GetEffectiveOutputCategoriesAsync(
                    dbContext,
                    source.Id,
                    contentType,
                    normalizedCategories.Select(category => category.UpstreamCategoryId).ToHashSet(StringComparer.Ordinal),
                    cancellationToken);

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
            return new CategoryAdministrationView(sources, null, selectedContentType, [], [], []);
        }

        var rules = await LoadRuleDefinitionsAsync(dbContext, effectiveSourceId.Value, selectedContentType, cancellationToken);

        var customCategories = await dbContext.CustomCategories
            .Where(category => category.ContentType == selectedContentType)
            .OrderBy(category => category.DisplayName)
            .ThenBy(category => category.Id)
            .Select(category => new CustomCategorySummary(
                category.Id,
                category.XtreamForgeCategoryId,
                category.DisplayName,
                category.UpstreamCategories.Count()))
            .ToListAsync(cancellationToken);

        var upstreamCategories = await dbContext.UpstreamCategories
            .Where(category => category.XtreamSourceId == effectiveSourceId.Value && category.ContentType == selectedContentType)
            .Include(category => category.DedicatedOutputCategory)
            .Include(category => category.CustomCategory)
            .OrderBy(category => category.UpstreamCategoryName)
            .ThenBy(category => category.UpstreamCategoryId)
            .ToListAsync(cancellationToken);

        var categorySummaries = upstreamCategories
            .Select(category =>
            {
                var effectiveState = EvaluateEffectiveState(category.IsExcluded, category.UpstreamCategoryName, rules);
                return new UpstreamCategorySummary(
                    category.Id,
                    category.UpstreamCategoryId,
                    category.UpstreamCategoryName,
                    category.IsExcluded,
                    category.CustomCategoryId,
                    category.CustomCategory?.DisplayName,
                    category.DedicatedOutputCategory.XtreamForgeCategoryId,
                    category.DedicatedOutputCategory.DisplayName,
                    effectiveState.EffectiveDecision,
                    effectiveState.RuleDecision,
                    effectiveState.MatchedRuleId,
                    effectiveState.MatchedRuleSequence,
                    effectiveState.MatchedRuleAction,
                    effectiveState.MatchedRuleOperator,
                    effectiveState.MatchedPattern,
                    effectiveState.MatchedRuleCaseSensitive,
                    effectiveState.EffectiveDecision == CategoryInclusionDecision.Include,
                    category.IsExcluded ? CategoryMappingSelection.Disabled : category.CustomCategoryId is null ? CategoryMappingSelection.Original : CategoryMappingSelection.Custom);
            })
            .ToList();

        return new CategoryAdministrationView(sources, effectiveSourceId, selectedContentType, customCategories, categorySummaries, rules);
    }

    public async Task<CategoryConfigurationResult> SaveCategoryConfigurationAsync(CategoryConfigurationCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        for (var attempt = 1; attempt <= MaxSyncAttempts; attempt++)
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            try
            {
                var upstreamCategory = await dbContext.UpstreamCategories
                    .Include(category => category.DedicatedOutputCategory)
                    .Include(category => category.CustomCategory)
                    .SingleOrDefaultAsync(
                        category => category.Id == command.UpstreamCategoryRecordId
                            && category.XtreamSourceId == command.SelectedSourceId
                            && category.ContentType == command.SelectedContentType,
                        cancellationToken);

                if (upstreamCategory is null)
                {
                    throw new InvalidOperationException("The category does not belong to the selected source or content type.");
                }

                switch (command.MappingSelection)
                {
                    case CategoryMappingSelection.Disabled:
                        upstreamCategory.IsExcluded = true;
                        break;
                    case CategoryMappingSelection.Original:
                        upstreamCategory.IsExcluded = false;
                        upstreamCategory.CustomCategoryId = null;
                        upstreamCategory.CustomCategory = null;
                        break;
                    case CategoryMappingSelection.Custom:
                        upstreamCategory.IsExcluded = false;
                        upstreamCategory.CustomCategory = await ResolveCustomCategoryAsync(dbContext, upstreamCategory.ContentType, command.CustomCategoryId, command.NewCustomCategoryName, cancellationToken);
                        break;
                    default:
                        throw new InvalidOperationException("Invalid category mapping selection.");
                }

                await dbContext.SaveChangesAsync(cancellationToken);
                return new CategoryConfigurationResult(upstreamCategory.XtreamSourceId, upstreamCategory.ContentType);
            }
            catch (DbUpdateException exception) when (attempt < MaxSyncAttempts && IsUniqueConstraintViolation(exception))
            {
            }
        }

        throw new InvalidOperationException("Unable to save the category configuration after multiple attempts.");
    }

    public async Task<CustomCategoryMutationResult> UpdateCustomCategoryAsync(CustomCategoryUpdateCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var customCategory = await dbContext.CustomCategories.SingleOrDefaultAsync(
            category => category.Id == command.CustomCategoryId && category.ContentType == command.ContentType,
            cancellationToken);

        if (customCategory is null)
        {
            throw new InvalidOperationException("Custom category was not found.");
        }

        var normalizedName = NormalizeCustomCategoryName(command.DisplayName);
        var existingCategory = await dbContext.CustomCategories
            .Where(category => category.ContentType == command.ContentType && category.Id != command.CustomCategoryId)
            .OrderBy(category => category.Id)
            .FirstOrDefaultAsync(category => category.NormalizedDisplayName == normalizedName, cancellationToken);

        if (existingCategory is not null)
        {
            throw new InvalidOperationException("A custom category with the same name already exists for this content type.");
        }

        customCategory.DisplayName = command.DisplayName.Trim();
        customCategory.NormalizedDisplayName = normalizedName;
        customCategory.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return new CustomCategoryMutationResult(customCategory.ContentType);
    }

    public async Task<CustomCategoryMutationResult> DeleteCustomCategoryAsync(CustomCategoryDeleteCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var customCategory = await dbContext.CustomCategories
            .Include(category => category.UpstreamCategories)
            .SingleOrDefaultAsync(category => category.Id == command.CustomCategoryId && category.ContentType == command.ContentType, cancellationToken);

        if (customCategory is null)
        {
            throw new InvalidOperationException("Custom category was not found.");
        }

        var usageCount = customCategory.UpstreamCategories.Count;
        if (usageCount > 0)
        {
            throw new InvalidOperationException($"Custom category is still referenced by {usageCount} mapping(s) and cannot be deleted.");
        }

        dbContext.CustomCategories.Remove(customCategory);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new CustomCategoryMutationResult(command.ContentType);
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
        XtreamSource source,
        ContentType contentType,
        IReadOnlyList<DiscoveredCategory> normalizedCategories,
        DateTimeOffset discoveredAt,
        CancellationToken cancellationToken)
    {
        var upstreamCategories = source.Id == 0
            ? new Dictionary<string, UpstreamCategory>(StringComparer.Ordinal)
            : await dbContext.UpstreamCategories
                .Where(category => category.XtreamSourceId == source.Id && category.ContentType == contentType)
                .Include(category => category.DedicatedOutputCategory)
                .ToDictionaryAsync(category => category.UpstreamCategoryId, StringComparer.Ordinal, cancellationToken);

        var nextXtreamForgeCategoryId = await GetNextXtreamForgeCategoryIdAsync(dbContext, contentType, cancellationToken);
        var nextSortOrder = source.Id == 0
            ? 0
            : await dbContext.OutputCategories
                .Where(category => category.XtreamSourceId == source.Id && category.ContentType == contentType)
                .Select(category => (int?)category.SortOrder)
                .MaxAsync(cancellationToken) ?? 0;

        foreach (var discoveredCategory in normalizedCategories)
        {
            if (!upstreamCategories.TryGetValue(discoveredCategory.UpstreamCategoryId, out var upstreamCategory))
            {
                var outputCategory = CreateOutputCategory(source, contentType, nextXtreamForgeCategoryId++, ++nextSortOrder, discoveredCategory.UpstreamCategoryName, discoveredAt);
                dbContext.OutputCategories.Add(outputCategory);

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

                dbContext.UpstreamCategories.Add(upstreamCategory);
                upstreamCategories.Add(upstreamCategory.UpstreamCategoryId, upstreamCategory);
                continue;
            }

            upstreamCategory.UpstreamCategoryName = discoveredCategory.UpstreamCategoryName;
            upstreamCategory.LastDiscoveredAtUtc = discoveredAt;

            if (upstreamCategory.DedicatedOutputCategory is null)
            {
                var outputCategory = CreateOutputCategory(source, contentType, nextXtreamForgeCategoryId++, ++nextSortOrder, discoveredCategory.UpstreamCategoryName, discoveredAt);
                dbContext.OutputCategories.Add(outputCategory);
                upstreamCategory.DedicatedOutputCategory = outputCategory;
            }
            else
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
            .Where(category => category.XtreamSourceId == sourceId && category.ContentType == contentType)
            .Include(category => category.DedicatedOutputCategory)
            .Include(category => category.CustomCategory)
            .OrderBy(category => category.DedicatedOutputCategory.SortOrder)
            .ThenBy(category => category.UpstreamCategoryName)
            .ToListAsync(cancellationToken);

        return upstreamCategories
            .Where(category => restrictToUpstreamCategoryIds is null || restrictToUpstreamCategoryIds.Contains(category.UpstreamCategoryId))
            .Select(category => new
            {
                Category = category,
                State = EvaluateEffectiveState(category.IsExcluded, category.UpstreamCategoryName, rules)
            })
            .Where(result => result.State.EffectiveDecision == CategoryInclusionDecision.Include)
            .GroupBy(
                result => result.Category.CustomCategoryId is int customCategoryId ? $"custom:{customCategoryId}" : $"original:{result.Category.DedicatedOutputCategoryId}",
                StringComparer.Ordinal)
            .Select(group =>
            {
                var first = group.First().Category;
                var outputId = first.CustomCategory?.XtreamForgeCategoryId ?? first.DedicatedOutputCategory.XtreamForgeCategoryId;
                var displayName = first.CustomCategory?.DisplayName ?? first.DedicatedOutputCategory.DisplayName;
                var sortOrder = group.Min(entry => entry.Category.DedicatedOutputCategory.SortOrder);

                return new EffectiveOutputCategoryMapping(
                    outputId,
                    sortOrder,
                    displayName,
                    group.Select(entry => entry.Category.UpstreamCategoryId).Distinct(StringComparer.Ordinal).ToList());
            })
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.XtreamForgeCategoryId)
            .ToList();
    }

    private EffectiveCategoryState EvaluateEffectiveState(
        bool isManuallyExcluded,
        string categoryName,
        IReadOnlyList<CategoryRuleDefinition> rules)
    {
        var ruleResult = ruleEvaluator.Evaluate(categoryName, rules);
        var effectiveDecision = isManuallyExcluded ? CategoryInclusionDecision.Exclude : ruleResult.Decision;

        return new EffectiveCategoryState(
            effectiveDecision,
            ruleResult.Decision,
            ruleResult.MatchedRuleId,
            ruleResult.MatchedRuleSequence,
            ruleResult.MatchedRuleAction,
            ruleResult.MatchedRuleOperator,
            ruleResult.MatchedPattern,
            ruleResult.MatchedRuleCaseSensitive);
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

    private async Task<CustomCategory> ResolveCustomCategoryAsync(
        XtreamForgeDbContext dbContext,
        ContentType contentType,
        int? customCategoryId,
        string? newCustomCategoryName,
        CancellationToken cancellationToken)
    {
        if (customCategoryId is int existingCategoryId)
        {
            var existingCategory = await dbContext.CustomCategories.SingleOrDefaultAsync(
                category => category.Id == existingCategoryId && category.ContentType == contentType,
                cancellationToken);

            return existingCategory ?? throw new InvalidOperationException("The selected custom category does not belong to the chosen content type.");
        }

        var normalizedName = NormalizeCustomCategoryName(newCustomCategoryName);
        var existingByName = await dbContext.CustomCategories
            .Where(category => category.ContentType == contentType)
            .OrderBy(category => category.Id)
            .FirstOrDefaultAsync(category => category.NormalizedDisplayName == normalizedName, cancellationToken);

        if (existingByName is not null)
        {
            return existingByName;
        }

        var nextCategoryId = await GetNextXtreamForgeCategoryIdAsync(dbContext, contentType, cancellationToken);
        var timestamp = DateTimeOffset.UtcNow;
        var customCategory = new CustomCategory
        {
            ContentType = contentType,
            XtreamForgeCategoryId = nextCategoryId,
            DisplayName = newCustomCategoryName!.Trim(),
            NormalizedDisplayName = normalizedName,
            CreatedAtUtc = timestamp,
            UpdatedAtUtc = timestamp
        };

        dbContext.CustomCategories.Add(customCategory);
        return customCategory;
    }

    private async Task<int> GetNextXtreamForgeCategoryIdAsync(
        XtreamForgeDbContext dbContext,
        ContentType contentType,
        CancellationToken cancellationToken)
    {
        var maxOutputCategoryId = await dbContext.OutputCategories
            .Where(category => category.ContentType == contentType)
            .Select(category => (int?)category.XtreamForgeCategoryId)
            .MaxAsync(cancellationToken) ?? 0;

        var maxCustomCategoryId = await dbContext.CustomCategories
            .Where(category => category.ContentType == contentType)
            .Select(category => (int?)category.XtreamForgeCategoryId)
            .MaxAsync(cancellationToken) ?? 0;

        return Math.Max(maxOutputCategoryId, maxCustomCategoryId) + 1;
    }

    private static string NormalizeCustomCategoryName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new InvalidOperationException("Custom category name is required.");
        }

        var trimmedName = name.Trim();
        if (trimmedName.Length > MaxCustomCategoryNameLength)
        {
            throw new InvalidOperationException($"Custom category name must be {MaxCustomCategoryNameLength} characters or fewer.");
        }

        return trimmedName.ToUpperInvariant();
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
    int XtreamForgeCategoryId,
    int SortOrder,
    string DisplayName,
    IReadOnlyList<string> IncludedUpstreamCategoryIds);

public sealed record XtreamSourceSummary(int Id, string Protocol, string Host, int Port, DateTimeOffset LastSeenAtUtc);

public sealed record CustomCategorySummary(int Id, int XtreamForgeCategoryId, string DisplayName, int UsageCount);

public sealed record UpstreamCategorySummary(
    int Id,
    string UpstreamCategoryId,
    string UpstreamCategoryName,
    bool IsManuallyExcluded,
    int? CustomCategoryId,
    string? CustomCategoryName,
    int DedicatedXtreamForgeCategoryId,
    string DedicatedOutputName,
    CategoryInclusionDecision EffectiveDecision,
    CategoryInclusionDecision RuleDecision,
    int? MatchedRuleId,
    int? MatchedRuleSequence,
    CategoryRuleAction? MatchedRuleAction,
    CategoryRuleOperator? MatchedRuleOperator,
    string? MatchedPattern,
    bool? MatchedRuleCaseSensitive,
    bool IsEffectivelyIncluded,
    CategoryMappingSelection CurrentMappingSelection);

public sealed record CategoryAdministrationView(
    IReadOnlyList<XtreamSourceSummary> Sources,
    int? SelectedSourceId,
    ContentType SelectedContentType,
    IReadOnlyList<CustomCategorySummary> CustomCategories,
    IReadOnlyList<UpstreamCategorySummary> UpstreamCategories,
    IReadOnlyList<CategoryRuleDefinition> Rules);

public sealed record CategoryConfigurationCommand(
    int UpstreamCategoryRecordId,
    int SelectedSourceId,
    ContentType SelectedContentType,
    CategoryMappingSelection MappingSelection,
    int? CustomCategoryId,
    string? NewCustomCategoryName);

public sealed record CategoryConfigurationResult(int SourceId, ContentType ContentType);

public sealed record CustomCategoryUpdateCommand(int CustomCategoryId, ContentType ContentType, string DisplayName);

public sealed record CustomCategoryDeleteCommand(int CustomCategoryId, ContentType ContentType);

public sealed record CustomCategoryMutationResult(ContentType ContentType);

public sealed record EffectiveCategoryState(
    CategoryInclusionDecision EffectiveDecision,
    CategoryInclusionDecision RuleDecision,
    int? MatchedRuleId,
    int? MatchedRuleSequence,
    CategoryRuleAction? MatchedRuleAction,
    CategoryRuleOperator? MatchedRuleOperator,
    string? MatchedPattern,
    bool? MatchedRuleCaseSensitive);

public enum CategoryMappingSelection
{
    Disabled = 1,
    Original = 2,
    Custom = 3
}
