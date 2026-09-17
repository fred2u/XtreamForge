using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using XtreamForge.Data;
using XtreamForge.Source;

namespace XtreamForge.Categories;

public sealed class XtreamCategoryMappingService(
    IDbContextFactory<XtreamForgeDbContext> dbContextFactory,
    SourceService sourceService,
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

        var synchronizedCategories = await sourceService.SynchronizeCategoriesAsync(
            sourceDescriptor,
            contentType,
            discoveredCategories,
            cancellationToken);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var rules = await LoadRuleDefinitionsAsync(dbContext, synchronizedCategories.SourceId, contentType, cancellationToken);

        var effectiveCategoryInputs = synchronizedCategories.Categories
            .Select(CreateEffectiveCategoryCandidate)
            .ToList();

        var effectiveCategories = BuildEffectiveOutputCategories(effectiveCategoryInputs, rules);

        return [.. effectiveCategories.Select(category => new RewrittenCategory(category.XtreamForgeCategoryId.ToString(), category.DisplayName, category.IncludedUpstreamCategoryIds))];
    }

    public async Task<CategoryAdministrationView> GetAdministrationViewAsync(
        int? selectedSourceId,
        ContentType selectedContentType,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var sources = await sourceService.GetSourcesAsync(cancellationToken);

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

        var upstreamCategories = (await sourceService.GetSourceCategoriesAsync(effectiveSourceId.Value, selectedContentType, cancellationToken: cancellationToken))
            .OrderBy(category => category.UpstreamCategoryName)
            .ThenBy(category => category.UpstreamCategoryId)
            .ToList();

        var categorySummaries = upstreamCategories
            .Select(category =>
            {
                var effectiveState = EvaluateEffectiveState(category.IsExcluded, category.UpstreamCategoryName, rules);
                return new UpstreamCategorySummary(
                    category.UpstreamCategoryRecordId,
                    category.UpstreamCategoryId,
                    category.UpstreamCategoryName,
                    category.IsExcluded,
                    category.CustomCategoryId,
                    category.CustomCategoryName,
                    category.DedicatedXtreamForgeCategoryId,
                    category.DedicatedOutputName,
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
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

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
                        upstreamCategory.CustomCategoryId = null;
                        upstreamCategory.CustomCategory = null;
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
                await transaction.CommitAsync(cancellationToken);
                CustomCategorySummary? customCategory = null;
                if (upstreamCategory.CustomCategoryId is int customCategoryId && upstreamCategory.CustomCategory is not null)
                {
                    customCategory = new CustomCategorySummary(
                        upstreamCategory.CustomCategory.Id,
                        upstreamCategory.CustomCategory.XtreamForgeCategoryId,
                        upstreamCategory.CustomCategory.DisplayName,
                        await dbContext.UpstreamCategories.CountAsync(category => category.CustomCategoryId == customCategoryId, cancellationToken));
                }

                return new CategoryConfigurationResult(
                    upstreamCategory.XtreamSourceId,
                    upstreamCategory.ContentType,
                    command.MappingSelection,
                    customCategory);
            }
            catch (DbUpdateException exception) when (attempt < MaxSyncAttempts && IsUniqueConstraintViolation(exception))
            {
                await transaction.RollbackAsync(CancellationToken.None);
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

    public async Task<CustomCategorySummary> CreateCustomCategoryAsync(CustomCategoryCreateCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        for (var attempt = 1; attempt <= MaxSyncAttempts; attempt++)
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            try
            {
                var customCategory = await ResolveCustomCategoryAsync(dbContext, command.ContentType, null, command.DisplayName, cancellationToken);
                await dbContext.SaveChangesAsync(cancellationToken);

                var usageCount = await dbContext.UpstreamCategories.CountAsync(
                    category => category.CustomCategoryId == customCategory.Id,
                    cancellationToken);

                return new CustomCategorySummary(
                    customCategory.Id,
                    customCategory.XtreamForgeCategoryId,
                    customCategory.DisplayName,
                    usageCount);
            }
            catch (DbUpdateException exception) when (attempt < MaxSyncAttempts && IsUniqueConstraintViolation(exception))
            {
            }
        }

        throw new InvalidOperationException("Unable to create the custom category after multiple attempts.");
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

    public async Task<IReadOnlyList<CustomCategoryUsageSummary>> GetCustomCategoryUsagesAsync(
        int customCategoryId,
        ContentType contentType,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        var categoryExists = await dbContext.CustomCategories
            .AsNoTracking()
            .AnyAsync(category => category.Id == customCategoryId && category.ContentType == contentType, cancellationToken);
        if (!categoryExists)
        {
            throw new InvalidOperationException("Custom category was not found.");
        }

        return await dbContext.UpstreamCategories
            .AsNoTracking()
            .Where(category => category.CustomCategoryId == customCategoryId && category.ContentType == contentType)
            .OrderBy(category => category.XtreamSource.Host)
            .ThenBy(category => category.XtreamSource.Port)
            .ThenBy(category => category.UpstreamCategoryName)
            .Select(category => new CustomCategoryUsageSummary(
                category.Id,
                category.XtreamSourceId,
                category.XtreamSource.Protocol,
                category.XtreamSource.Host,
                category.XtreamSource.Port,
                category.ContentType,
                category.UpstreamCategoryId,
                category.UpstreamCategoryName))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<EffectiveOutputCategoryMapping>> GetEffectiveOutputCategoryMappingsAsync(
        XtreamSourceDescriptor sourceDescriptor,
        ContentType contentType,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sourceDescriptor);

        var sourceId = await sourceService.GetSourceIdAsync(sourceDescriptor, cancellationToken);
        if (sourceId is not int resolvedSourceId)
        {
            return [];
        }

        return await GetEffectiveOutputCategoryMappingsAsync(resolvedSourceId, contentType, cancellationToken);
    }

    public async Task<IReadOnlyList<EffectiveOutputCategoryMapping>> GetEffectiveOutputCategoryMappingsAsync(
        int sourceId,
        ContentType contentType,
        CancellationToken cancellationToken = default)
    {
        var upstreamCategories = await sourceService.GetSourceCategoriesAsync(sourceId, contentType, cancellationToken: cancellationToken);
        return await GetEffectiveOutputCategoriesAsync(sourceId, contentType, upstreamCategories, cancellationToken);
    }

    private async Task<List<EffectiveOutputCategoryMapping>> GetEffectiveOutputCategoriesAsync(
        int sourceId,
        ContentType contentType,
        IReadOnlyList<SourceCategorySnapshot> upstreamCategories,
        CancellationToken cancellationToken)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rules = await LoadRuleDefinitionsAsync(dbContext, sourceId, contentType, cancellationToken);
        return BuildEffectiveOutputCategories([.. upstreamCategories.Select(CreateEffectiveCategoryCandidate)], rules);
    }

    private List<EffectiveOutputCategoryMapping> BuildEffectiveOutputCategories(
        IReadOnlyList<EffectiveCategoryCandidate> effectiveCategoryCandidates,
        IReadOnlyList<CategoryRuleDefinition> rules)
    {
        return [.. effectiveCategoryCandidates
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
                var outputId = first.CustomXtreamForgeCategoryId ?? first.DedicatedXtreamForgeCategoryId;
                var displayName = first.CustomDisplayName ?? first.DedicatedDisplayName;
                var sortOrder = group.Min(entry => entry.Category.DedicatedSortOrder);

                return new EffectiveOutputCategoryMapping(
                    outputId,
                    sortOrder,
                    displayName,
                    group.Select(entry => entry.Category.UpstreamCategoryId).Distinct(StringComparer.Ordinal).ToList());
            })
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.XtreamForgeCategoryId)];
    }

    private EffectiveCategoryState EvaluateEffectiveState(
        bool isManuallyExcluded,
        string categoryName,
        IReadOnlyList<CategoryRuleDefinition> rules)
    {
        var ruleResult = ruleEvaluator.EvaluateOrdered(categoryName, rules);
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

    private async static Task<CustomCategory> ResolveCustomCategoryAsync(
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

    private async static Task<int> GetNextXtreamForgeCategoryIdAsync(
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

    private static EffectiveCategoryCandidate CreateEffectiveCategoryCandidate(SourceCategorySnapshot category) =>
        new(
            category.UpstreamCategoryId,
            category.UpstreamCategoryName,
            category.IsExcluded,
            category.DedicatedOutputCategoryRecordId,
            category.DedicatedXtreamForgeCategoryId,
            category.DedicatedOutputName,
            category.DedicatedOutputSortOrder,
            category.CustomCategoryId,
            category.CustomXtreamForgeCategoryId,
            category.CustomCategoryName);

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

    private static async Task<List<CategoryRuleDefinition>> LoadRuleDefinitionsAsync(
        XtreamForgeDbContext dbContext,
        int sourceId,
        ContentType contentType,
        CancellationToken cancellationToken)
    {
        return await dbContext.CategoryRules
            .AsNoTracking()
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

    private sealed record EffectiveCategoryCandidate(
        string UpstreamCategoryId,
        string UpstreamCategoryName,
        bool IsExcluded,
        int DedicatedOutputCategoryId,
        int DedicatedXtreamForgeCategoryId,
        string DedicatedDisplayName,
        int DedicatedSortOrder,
        int? CustomCategoryId,
        int? CustomXtreamForgeCategoryId,
        string? CustomDisplayName);
}

public sealed record DiscoveredCategory(string UpstreamCategoryId, string UpstreamCategoryName);

public sealed record RewrittenCategory(string CategoryId, string CategoryName, IReadOnlyList<string> IncludedUpstreamCategoryIds);

public sealed record EffectiveOutputCategoryMapping(
    int XtreamForgeCategoryId,
    int SortOrder,
    string DisplayName,
    IReadOnlyList<string> IncludedUpstreamCategoryIds);

public sealed record CustomCategoryUsageSummary(
    int UpstreamCategoryRecordId,
    int SourceId,
    string SourceProtocol,
    string SourceHost,
    int SourcePort,
    ContentType ContentType,
    string UpstreamCategoryId,
    string UpstreamCategoryName);

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

public sealed record CategoryConfigurationResult(
    int SourceId,
    ContentType ContentType,
    CategoryMappingSelection MappingSelection,
    CustomCategorySummary? CustomCategory);
public sealed record CustomCategoryCreateCommand(ContentType ContentType, string? DisplayName);
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
