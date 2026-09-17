using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using System.Diagnostics;
using XtreamForge.Data;
using XtreamForge.Categories;

namespace XtreamForge.Categories;

public sealed class XtreamCategoryMappingService(
    IDbContextFactory<XtreamForgeDbContext> dbContextFactory,
    CategoryRuleEvaluator ruleEvaluator)
{
    private static readonly ActivitySource ActivitySource = new("XtreamForge");
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
                XtreamSource source;
                using (var activity = ActivitySource.StartActivity("Xtream.Categories.SourceLookup", ActivityKind.Internal))
                {
                    activity?.SetTag("xtream.content_type", contentType.ToString());
                    source = await GetOrCreateSourceAsync(dbContext, sourceDescriptor, discoveredAt, cancellationToken);
                    activity?.SetTag("xtream.source.id", source.Id);
                }

                SynchronizedCategoryBatch synchronizedBatch;
                using (var activity = ActivitySource.StartActivity("Xtream.Categories.Sync", ActivityKind.Internal))
                {
                    activity?.SetTag("xtream.content_type", contentType.ToString());
                    activity?.SetTag("xtream.categories.discovered_count", normalizedCategories.Count);
                    synchronizedBatch = await SynchronizeUpstreamCategoriesAsync(
                        dbContext,
                        source,
                        contentType,
                        normalizedCategories,
                        discoveredAt,
                        cancellationToken);
                    activity?.SetTag("xtream.categories.request_count", synchronizedBatch.RequestCategories.Count);
                    activity?.SetTag("xtream.categories.created_count", synchronizedBatch.CreatedCategoryCount);
                    activity?.SetTag("xtream.categories.updated_count", synchronizedBatch.UpdatedCategoryCount);
                }

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

                IReadOnlyList<CategoryRuleDefinition> rules;
                using (var activity = ActivitySource.StartActivity("Xtream.Categories.Rules", ActivityKind.Internal))
                {
                    activity?.SetTag("xtream.content_type", contentType.ToString());
                    rules = await LoadRuleDefinitionsAsync(dbContext, source.Id, contentType, cancellationToken);
                    activity?.SetTag("xtream.rules.count", rules.Count);
                }

                var effectiveCategoryInputs = synchronizedBatch.RequestCategories
                    .Select(CreateEffectiveCategoryCandidate)
                    .ToList();

                using (var activity = ActivitySource.StartActivity("Xtream.Categories.Mappings", ActivityKind.Internal))
                {
                    activity?.SetTag("xtream.content_type", contentType.ToString());
                    activity?.SetTag("xtream.categories.request_count", effectiveCategoryInputs.Count);
                    activity?.SetTag(
                        "xtream.categories.custom_mapping_count",
                        effectiveCategoryInputs.Count(category => category.CustomCategoryId is not null));
                }

                IReadOnlyList<EffectiveOutputCategoryMapping> effectiveCategories;
                using (var activity = ActivitySource.StartActivity("Xtream.Categories.Evaluate", ActivityKind.Internal))
                {
                    activity?.SetTag("xtream.content_type", contentType.ToString());
                    effectiveCategories = BuildEffectiveOutputCategories(effectiveCategoryInputs, rules);
                    activity?.SetTag("xtream.categories.output_count", effectiveCategories.Count);
                }

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

    private async Task<SynchronizedCategoryBatch> SynchronizeUpstreamCategoriesAsync(
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

    private async Task<IReadOnlyList<EffectiveOutputCategoryMapping>> GetEffectiveOutputCategoriesAsync(
        XtreamForgeDbContext dbContext,
        int sourceId,
        ContentType contentType,
        ISet<string>? restrictToUpstreamCategoryIds,
        CancellationToken cancellationToken)
    {
        var rules = await LoadRuleDefinitionsAsync(dbContext, sourceId, contentType, cancellationToken);
        var restrictedIds = restrictToUpstreamCategoryIds?.ToArray();

        if (restrictedIds is { Length: 0 })
        {
            return [];
        }

        var query = dbContext.UpstreamCategories
            .AsNoTracking()
            .Where(category => category.XtreamSourceId == sourceId && category.ContentType == contentType);

        if (restrictedIds is { Length: > 0 })
        {
            query = query.Where(category => restrictedIds.Contains(category.UpstreamCategoryId));
        }

        var upstreamCategories = await query
            .OrderBy(category => category.DedicatedOutputCategory.SortOrder)
            .ThenBy(category => category.UpstreamCategoryName)
            .Select(category => new EffectiveCategoryCandidate(
                category.UpstreamCategoryId,
                category.UpstreamCategoryName,
                category.IsExcluded,
                category.DedicatedOutputCategoryId,
                category.DedicatedOutputCategory.XtreamForgeCategoryId,
                category.DedicatedOutputCategory.DisplayName,
                category.DedicatedOutputCategory.SortOrder,
                category.CustomCategoryId,
                category.CustomCategory != null ? category.CustomCategory.XtreamForgeCategoryId : null,
                category.CustomCategory != null ? category.CustomCategory.DisplayName : null))
            .ToListAsync(cancellationToken);

        return BuildEffectiveOutputCategories(upstreamCategories, rules);
    }

    private IReadOnlyList<EffectiveOutputCategoryMapping> BuildEffectiveOutputCategories(
        IReadOnlyList<EffectiveCategoryCandidate> upstreamCategories,
        IReadOnlyList<CategoryRuleDefinition> rules) =>
        upstreamCategories
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
            .ThenBy(category => category.XtreamForgeCategoryId)
            .ToList();

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

    private static EffectiveCategoryCandidate CreateEffectiveCategoryCandidate(UpstreamCategory category) =>
        new(
            category.UpstreamCategoryId,
            category.UpstreamCategoryName,
            category.IsExcluded,
            category.DedicatedOutputCategoryId,
            category.DedicatedOutputCategory.XtreamForgeCategoryId,
            category.DedicatedOutputCategory.DisplayName,
            category.DedicatedOutputCategory.SortOrder,
            category.CustomCategoryId,
            category.CustomCategory?.XtreamForgeCategoryId,
            category.CustomCategory?.DisplayName);

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

    private sealed record SynchronizedCategoryBatch(
        IReadOnlyList<UpstreamCategory> RequestCategories,
        int CreatedCategoryCount,
        int UpdatedCategoryCount,
        IReadOnlyList<int> ExistingCategoryIdsToTouch,
        bool TouchAllExistingCategories);

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

public sealed record XtreamSourceDescriptor(string Protocol, string Host, int Port);

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
