using Microsoft.EntityFrameworkCore;
using XtreamForge.Data;
using XtreamForge.Categories;

namespace XtreamForge.Categories;

public sealed class CategoryRuleService(
    IDbContextFactory<XtreamForgeDbContext> dbContextFactory,
    CategoryRuleEvaluator evaluator)
{
    public const int SequenceStep = 10;
    public const int MaxPatternLength = 255;

    public async Task<IReadOnlyList<CategoryRuleDefinition>> GetRuleDefinitionsAsync(
        int sourceId,
        ContentType contentType,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await LoadRuleDefinitionsAsync(dbContext, sourceId, contentType, cancellationToken);
    }

    public async Task<CategoryRulesAdministrationView> GetAdministrationViewAsync(
        int? selectedSourceId,
        ContentType selectedContentType,
        string? previewCategoryName,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);

        if (selectedSourceId is null)
        {
            return new CategoryRulesAdministrationView([], previewCategoryName, null);
        }

        var rules = await dbContext.CategoryRules
            .Where(rule => rule.XtreamSourceId == selectedSourceId.Value && rule.ContentType == selectedContentType)
            .OrderBy(rule => rule.Sequence)
            .ThenBy(rule => rule.Id)
            .Select(rule => new CategoryRuleSummary(
                rule.Id,
                rule.Sequence,
                rule.Action,
                rule.Operator,
                rule.Pattern,
                rule.CaseSensitive,
                rule.IsEnabled))
            .ToListAsync(cancellationToken);

        CategoryRulePreviewResult? preview = null;
        if (!string.IsNullOrWhiteSpace(previewCategoryName))
        {
            var evaluation = evaluator.Evaluate(
                previewCategoryName,
                rules.Select(rule => new CategoryRuleDefinition(
                    rule.Id,
                    rule.Sequence,
                    rule.Action,
                    rule.Operator,
                    rule.Pattern,
                    rule.CaseSensitive,
                    rule.IsEnabled)).ToList());

            preview = new CategoryRulePreviewResult(previewCategoryName, evaluation);
        }

        return new CategoryRulesAdministrationView(rules, previewCategoryName, preview);
    }

    public async Task<CategoryRuleMutationResult> CreateRuleAsync(CategoryRuleEditorCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var normalizedPattern = ValidateAndNormalizePattern(command.Pattern);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSourceExistsAsync(dbContext, command.SelectedSourceId, cancellationToken);

        var nextSequence = await dbContext.CategoryRules
            .Where(rule => rule.XtreamSourceId == command.SelectedSourceId && rule.ContentType == command.SelectedContentType)
            .Select(rule => (int?)rule.Sequence)
            .MaxAsync(cancellationToken) ?? 0;

        var timestamp = DateTimeOffset.UtcNow;
        dbContext.CategoryRules.Add(new CategoryRule
        {
            XtreamSourceId = command.SelectedSourceId,
            ContentType = command.SelectedContentType,
            Sequence = nextSequence + SequenceStep,
            Action = command.Action,
            Operator = command.Operator,
            Pattern = normalizedPattern,
            CaseSensitive = command.CaseSensitive,
            IsEnabled = command.IsEnabled,
            CreatedAtUtc = timestamp,
            UpdatedAtUtc = timestamp
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return new CategoryRuleMutationResult(command.SelectedSourceId, command.SelectedContentType);
    }

    public async Task<CategoryRuleMutationResult> UpdateRuleAsync(CategoryRuleEditorCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.RuleId is null)
        {
            throw new InvalidOperationException("Rule ID is required when updating a category rule.");
        }

        var normalizedPattern = ValidateAndNormalizePattern(command.Pattern);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rule = await dbContext.CategoryRules.SingleOrDefaultAsync(existingRule => existingRule.Id == command.RuleId.Value, cancellationToken);
        if (rule is null)
        {
            throw new InvalidOperationException("Category rule was not found.");
        }

        EnsureRuleScope(rule, command.SelectedSourceId, command.SelectedContentType);

        rule.Action = command.Action;
        rule.Operator = command.Operator;
        rule.Pattern = normalizedPattern;
        rule.CaseSensitive = command.CaseSensitive;
        rule.IsEnabled = command.IsEnabled;
        rule.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return new CategoryRuleMutationResult(rule.XtreamSourceId, rule.ContentType);
    }

    public async Task<CategoryRuleMutationResult> DeleteRuleAsync(CategoryRuleDeleteCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!command.ConfirmDelete)
        {
            throw new InvalidOperationException("Confirm delete before removing a rule.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var rules = await dbContext.CategoryRules
            .Where(rule => rule.XtreamSourceId == command.SelectedSourceId && rule.ContentType == command.SelectedContentType)
            .OrderBy(rule => rule.Sequence)
            .ThenBy(rule => rule.Id)
            .ToListAsync(cancellationToken);

        var rule = rules.SingleOrDefault(existingRule => existingRule.Id == command.RuleId);
        if (rule is null)
        {
            throw new InvalidOperationException("Category rule was not found.");
        }

        dbContext.CategoryRules.Remove(rule);
        rules.Remove(rule);
        await dbContext.SaveChangesAsync(cancellationToken);

        await ReassignSequencesAsync(dbContext, rules, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new CategoryRuleMutationResult(command.SelectedSourceId, command.SelectedContentType);
    }

    public async Task<CategoryRuleMutationResult> ReorderRulesAsync(CategoryRuleOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var rules = await dbContext.CategoryRules
            .Where(rule => rule.XtreamSourceId == command.SelectedSourceId && rule.ContentType == command.SelectedContentType)
            .OrderBy(rule => rule.Sequence)
            .ThenBy(rule => rule.Id)
            .ToListAsync(cancellationToken);

        if (command.OrderedRuleIds.Count == 0)
        {
            throw new InvalidOperationException("At least one category rule ID is required.");
        }

        var duplicateRuleIds = command.OrderedRuleIds
            .GroupBy(ruleId => ruleId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicateRuleIds.Count > 0)
        {
            throw new InvalidOperationException("Category rule IDs must be unique.");
        }

        if (rules.Count != command.OrderedRuleIds.Count)
        {
            throw new InvalidOperationException("The reorder request must include every category rule for the selected source and content type.");
        }

        var existingRuleIds = rules.Select(rule => rule.Id).ToHashSet();
        if (command.OrderedRuleIds.Any(ruleId => !existingRuleIds.Contains(ruleId)))
        {
            throw new InvalidOperationException("One or more category rules do not belong to the selected source or content type.");
        }

        var orderByRuleId = command.OrderedRuleIds
            .Select((ruleId, index) => new { ruleId, index })
            .ToDictionary(item => item.ruleId, item => item.index);
        rules.Sort((left, right) => orderByRuleId[left.Id].CompareTo(orderByRuleId[right.Id]));

        await ReassignSequencesAsync(dbContext, rules, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new CategoryRuleMutationResult(command.SelectedSourceId, command.SelectedContentType);
    }

    public async Task<CategoryRulePreviewResult?> PreviewAsync(
        int? sourceId,
        ContentType contentType,
        string? categoryName,
        CancellationToken cancellationToken = default)
    {
        if (sourceId is null || string.IsNullOrWhiteSpace(categoryName))
        {
            return null;
        }

        var rules = await GetRuleDefinitionsAsync(sourceId.Value, contentType, cancellationToken);
        var evaluation = evaluator.Evaluate(categoryName, rules);
        return new CategoryRulePreviewResult(categoryName, evaluation);
    }

    private static string ValidateAndNormalizePattern(string? pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            throw new ArgumentException("Rule pattern is required.", nameof(pattern));
        }

        var normalizedPattern = pattern.Trim();
        if (normalizedPattern.Length > MaxPatternLength)
        {
            throw new ArgumentException($"Rule pattern must be {MaxPatternLength} characters or fewer.", nameof(pattern));
        }

        return normalizedPattern;
    }

    private static async Task EnsureSourceExistsAsync(XtreamForgeDbContext dbContext, int sourceId, CancellationToken cancellationToken)
    {
        var exists = await dbContext.XtreamSources.AnyAsync(source => source.Id == sourceId, cancellationToken);
        if (!exists)
        {
            throw new InvalidOperationException("Xtream source was not found.");
        }
    }

    private static void EnsureRuleScope(CategoryRule rule, int sourceId, ContentType contentType)
    {
        if (rule.XtreamSourceId != sourceId || rule.ContentType != contentType)
        {
            throw new InvalidOperationException("The category rule does not belong to the selected source or content type.");
        }
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

    private static async Task ReassignSequencesAsync(
        XtreamForgeDbContext dbContext,
        List<CategoryRule> orderedRules,
        CancellationToken cancellationToken)
    {
        var timestamp = DateTimeOffset.UtcNow;
        for (var index = 0; index < orderedRules.Count; index++)
        {
            orderedRules[index].Sequence = -1 - index;
            orderedRules[index].UpdatedAtUtc = timestamp;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        for (var index = 0; index < orderedRules.Count; index++)
        {
            orderedRules[index].Sequence = (index + 1) * SequenceStep;
            orderedRules[index].UpdatedAtUtc = timestamp;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
    }
}

public sealed record CategoryRuleSummary(
    int Id,
    int Sequence,
    CategoryRuleAction Action,
    CategoryRuleOperator Operator,
    string Pattern,
    bool CaseSensitive,
    bool IsEnabled);

public sealed record CategoryRulesAdministrationView(
    IReadOnlyList<CategoryRuleSummary> Rules,
    string? PreviewCategoryName,
    CategoryRulePreviewResult? Preview);

public sealed record CategoryRulePreviewResult(
    string CategoryName,
    CategoryRuleEvaluationResult Evaluation);

public sealed record CategoryRuleEditorCommand(
    int? RuleId,
    int SelectedSourceId,
    ContentType SelectedContentType,
    CategoryRuleAction Action,
    CategoryRuleOperator Operator,
    string? Pattern,
    bool CaseSensitive,
    bool IsEnabled);

public record CategoryRuleIdentityCommand(
    int RuleId,
    int SelectedSourceId,
    ContentType SelectedContentType);

public sealed record CategoryRuleOrderCommand(
    int SelectedSourceId,
    ContentType SelectedContentType,
    IReadOnlyList<int> OrderedRuleIds);

public sealed record CategoryRuleDeleteCommand(
    int RuleId,
    int SelectedSourceId,
    ContentType SelectedContentType,
    bool ConfirmDelete) : CategoryRuleIdentityCommand(RuleId, SelectedSourceId, SelectedContentType);

public sealed record CategoryRuleMutationResult(
    int SourceId,
    ContentType ContentType);
