using Microsoft.EntityFrameworkCore;
using XtreamForge.Data;
using XtreamForge.Source;

namespace XtreamForge.Categories;

public sealed class CategoryService(
    IDbContextFactory<XtreamForgeDbContext> dbContextFactory,
    SourceService sourceService,
    CategoryRuleService categoryRuleService,
    XtreamCategoryMappingService categoryMappingService)
{
    private const int MaxSyncAttempts = 3;
    public const int MaxCustomCategoryNameLength = 255;

    public async Task<IReadOnlyList<RewrittenCategory>> RewriteCategoriesAsync(
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
        var rules = await categoryRuleService.GetRuleDefinitionsAsync(
            synchronizedCategories.SourceId,
            contentType,
            cancellationToken);

        return categoryMappingService.BuildRewrittenCategories(synchronizedCategories.Categories, rules);
    }

    public async Task<CategoryAdministrationView> GetAdministrationViewAsync(
        int? selectedSourceId,
        ContentType selectedContentType,
        CancellationToken cancellationToken = default)
    {
        var sources = await sourceService.GetSourcesAsync(cancellationToken);
        var effectiveSourceId = selectedSourceId ?? sources.FirstOrDefault()?.Id;
        if (effectiveSourceId is null)
        {
            return new CategoryAdministrationView(sources, null, selectedContentType, [], [], []);
        }

        var rules = await categoryRuleService.GetRuleDefinitionsAsync(
            effectiveSourceId.Value,
            selectedContentType,
            cancellationToken);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

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

        var upstreamCategories = (await sourceService.GetSourceCategoriesAsync(
                effectiveSourceId.Value,
                selectedContentType,
                cancellationToken: cancellationToken))
            .OrderBy(category => category.UpstreamCategoryName)
            .ThenBy(category => category.UpstreamCategoryId)
            .ToList();

        var categorySummaries = upstreamCategories
            .Select(category =>
            {
                var effectiveState = categoryMappingService.EvaluateEffectiveState(
                    category.IsExcluded,
                    category.UpstreamCategoryName,
                    rules);

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
                    category.IsExcluded
                        ? CategoryMappingSelection.Disabled
                        : category.CustomCategoryId is null
                            ? CategoryMappingSelection.Original
                            : CategoryMappingSelection.Custom);
            })
            .ToList();

        return new CategoryAdministrationView(
            sources,
            effectiveSourceId,
            selectedContentType,
            customCategories,
            categorySummaries,
            rules);
    }

    public async Task<CategoryConfigurationResult> SaveCategoryConfigurationAsync(
        CategoryConfigurationCommand command,
        CancellationToken cancellationToken = default)
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
                        upstreamCategory.CustomCategory = await ResolveCustomCategoryAsync(
                            dbContext,
                            upstreamCategory.ContentType,
                            command.CustomCategoryId,
                            command.NewCustomCategoryName,
                            cancellationToken);
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
                        await dbContext.UpstreamCategories.CountAsync(
                            category => category.CustomCategoryId == customCategoryId,
                            cancellationToken));
                }

                return new CategoryConfigurationResult(
                    upstreamCategory.XtreamSourceId,
                    upstreamCategory.ContentType,
                    command.MappingSelection,
                    customCategory);
            }
            catch (DbUpdateException exception) when (attempt < MaxSyncAttempts && CategoryPersistenceUtilities.IsUniqueConstraintViolation(exception))
            {
                await transaction.RollbackAsync(CancellationToken.None);
            }
        }

        throw new InvalidOperationException("Unable to save the category configuration after multiple attempts.");
    }

    public async Task<CustomCategoryMutationResult> UpdateCustomCategoryAsync(
        CustomCategoryUpdateCommand command,
        CancellationToken cancellationToken = default)
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

    public async Task<CustomCategorySummary> CreateCustomCategoryAsync(
        CustomCategoryCreateCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        for (var attempt = 1; attempt <= MaxSyncAttempts; attempt++)
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

            try
            {
                var customCategory = await ResolveCustomCategoryAsync(
                    dbContext,
                    command.ContentType,
                    null,
                    command.DisplayName,
                    cancellationToken);
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
            catch (DbUpdateException exception) when (attempt < MaxSyncAttempts && CategoryPersistenceUtilities.IsUniqueConstraintViolation(exception))
            {
            }
        }

        throw new InvalidOperationException("Unable to create the custom category after multiple attempts.");
    }

    public async Task<CustomCategoryMutationResult> DeleteCustomCategoryAsync(
        CustomCategoryDeleteCommand command,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var customCategory = await dbContext.CustomCategories
            .Include(category => category.UpstreamCategories)
            .SingleOrDefaultAsync(
                category => category.Id == command.CustomCategoryId && category.ContentType == command.ContentType,
                cancellationToken);

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
        var upstreamCategories = await sourceService.GetSourceCategoriesAsync(
            sourceId,
            contentType,
            cancellationToken: cancellationToken);
        var rules = await categoryRuleService.GetRuleDefinitionsAsync(sourceId, contentType, cancellationToken);

        return categoryMappingService.BuildEffectiveOutputCategoryMappings(upstreamCategories, rules);
    }

    private static async Task<CustomCategory> ResolveCustomCategoryAsync(
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

        var nextCategoryId = await CategoryPersistenceUtilities.GetNextXtreamForgeCategoryIdAsync(
            dbContext,
            contentType,
            cancellationToken);
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
}
