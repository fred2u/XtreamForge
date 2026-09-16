using Microsoft.EntityFrameworkCore;
using XtreamForge.Categories;
using XtreamForge.Data;

namespace XtreamForge.Items;

public sealed class ItemRuleService(
    IDbContextFactory<XtreamForgeDbContext> dbContextFactory,
    ItemRuleEvaluator evaluator)
{
    public const int SequenceStep = 10;
    public const int MaxPatternLength = 255;

    public async Task<IReadOnlyList<ItemRuleDefinition>> GetRuleDefinitionsAsync(
        int sourceId,
        ContentType contentType,
        CancellationToken cancellationToken = default)
    {
        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        return await LoadRuleDefinitionsAsync(dbContext, sourceId, contentType, cancellationToken);
    }

    public async Task<ItemRuleSet> GetRuleSetAsync(
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

        if (sourceId is null)
        {
            return new ItemRuleSet(null, []);
        }

        var rules = await LoadRuleDefinitionsAsync(dbContext, sourceId.Value, contentType, cancellationToken);
        return new ItemRuleSet(sourceId, rules);
    }

    public async Task<ItemRulesAdministrationView> GetAdministrationViewAsync(
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
            return new ItemRulesAdministrationView(sources, null, selectedContentType, []);
        }

        var rules = await dbContext.ItemRules
            .Where(rule => rule.XtreamSourceId == effectiveSourceId.Value && rule.ContentType == selectedContentType)
            .OrderBy(rule => rule.Sequence)
            .ThenBy(rule => rule.Id)
            .Select(rule => new ItemRuleSummary(
                rule.Id,
                rule.Sequence,
                rule.Field,
                rule.Action,
                rule.Operator,
                rule.Pattern,
                rule.CaseSensitive,
                rule.IsEnabled))
            .ToListAsync(cancellationToken);

        return new ItemRulesAdministrationView(sources, effectiveSourceId, selectedContentType, rules);
    }

    public async Task<ItemRuleMutationResult> CreateRuleAsync(ItemRuleEditorCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var normalizedPattern = ValidateAndNormalizePattern(command.Pattern);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await EnsureSourceExistsAsync(dbContext, command.SelectedSourceId, cancellationToken);

        var nextSequence = await dbContext.ItemRules
            .Where(rule => rule.XtreamSourceId == command.SelectedSourceId && rule.ContentType == command.SelectedContentType)
            .Select(rule => (int?)rule.Sequence)
            .MaxAsync(cancellationToken) ?? 0;

        var timestamp = DateTimeOffset.UtcNow;
        dbContext.ItemRules.Add(new ItemRule
        {
            XtreamSourceId = command.SelectedSourceId,
            ContentType = command.SelectedContentType,
            Sequence = nextSequence + SequenceStep,
            Field = command.Field,
            Action = command.Action,
            Operator = command.Operator,
            Pattern = normalizedPattern,
            CaseSensitive = command.CaseSensitive,
            IsEnabled = command.IsEnabled,
            CreatedAtUtc = timestamp,
            UpdatedAtUtc = timestamp
        });

        await dbContext.SaveChangesAsync(cancellationToken);
        return new ItemRuleMutationResult(command.SelectedSourceId, command.SelectedContentType);
    }

    public async Task<ItemRuleMutationResult> UpdateRuleAsync(ItemRuleEditorCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.RuleId is null)
        {
            throw new InvalidOperationException("Rule ID is required when updating an item rule.");
        }

        var normalizedPattern = ValidateAndNormalizePattern(command.Pattern);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var rule = await dbContext.ItemRules.SingleOrDefaultAsync(existingRule => existingRule.Id == command.RuleId.Value, cancellationToken);
        if (rule is null)
        {
            throw new InvalidOperationException("Item rule was not found.");
        }

        EnsureRuleScope(rule, command.SelectedSourceId, command.SelectedContentType);

        rule.Field = command.Field;
        rule.Action = command.Action;
        rule.Operator = command.Operator;
        rule.Pattern = normalizedPattern;
        rule.CaseSensitive = command.CaseSensitive;
        rule.IsEnabled = command.IsEnabled;
        rule.UpdatedAtUtc = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return new ItemRuleMutationResult(rule.XtreamSourceId, rule.ContentType);
    }

    public async Task<ItemRuleMutationResult> DeleteRuleAsync(ItemRuleDeleteCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!command.ConfirmDelete)
        {
            throw new InvalidOperationException("Confirm delete before removing a rule.");
        }

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var rules = await dbContext.ItemRules
            .Where(rule => rule.XtreamSourceId == command.SelectedSourceId && rule.ContentType == command.SelectedContentType)
            .OrderBy(rule => rule.Sequence)
            .ThenBy(rule => rule.Id)
            .ToListAsync(cancellationToken);

        var rule = rules.SingleOrDefault(existingRule => existingRule.Id == command.RuleId);
        if (rule is null)
        {
            throw new InvalidOperationException("Item rule was not found.");
        }

        dbContext.ItemRules.Remove(rule);
        rules.Remove(rule);
        await dbContext.SaveChangesAsync(cancellationToken);

        await ReassignSequencesAsync(dbContext, rules, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new ItemRuleMutationResult(command.SelectedSourceId, command.SelectedContentType);
    }

    public async Task<ItemRuleMutationResult> ReorderRulesAsync(ItemRuleOrderCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);

        await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var rules = await dbContext.ItemRules
            .Where(rule => rule.XtreamSourceId == command.SelectedSourceId && rule.ContentType == command.SelectedContentType)
            .OrderBy(rule => rule.Sequence)
            .ThenBy(rule => rule.Id)
            .ToListAsync(cancellationToken);

        if (command.OrderedRuleIds.Count == 0)
        {
            throw new InvalidOperationException("At least one item rule ID is required.");
        }

        var duplicateRuleIds = command.OrderedRuleIds
            .GroupBy(ruleId => ruleId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        if (duplicateRuleIds.Count > 0)
        {
            throw new InvalidOperationException("Item rule IDs must be unique.");
        }

        if (rules.Count != command.OrderedRuleIds.Count)
        {
            throw new InvalidOperationException("The reorder request must include every item rule for the selected source and content type.");
        }

        var existingRuleIds = rules.Select(rule => rule.Id).ToHashSet();
        if (command.OrderedRuleIds.Any(ruleId => !existingRuleIds.Contains(ruleId)))
        {
            throw new InvalidOperationException("One or more item rules do not belong to the selected source or content type.");
        }

        var orderByRuleId = command.OrderedRuleIds
            .Select((ruleId, index) => new { ruleId, index })
            .ToDictionary(item => item.ruleId, item => item.index);
        rules.Sort((left, right) => orderByRuleId[left.Id].CompareTo(orderByRuleId[right.Id]));

        await ReassignSequencesAsync(dbContext, rules, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new ItemRuleMutationResult(command.SelectedSourceId, command.SelectedContentType);
    }

    public async Task<ItemRulePreviewResult?> PreviewAsync(
        int? sourceId,
        ContentType contentType,
        string? itemName,
        CancellationToken cancellationToken = default)
    {
        if (sourceId is null || string.IsNullOrWhiteSpace(itemName))
        {
            return null;
        }

        var rules = await GetRuleDefinitionsAsync(sourceId.Value, contentType, cancellationToken);
        var evaluation = evaluator.Evaluate(new ItemRuleInput(itemName), rules);
        return new ItemRulePreviewResult(itemName, evaluation);
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

    private static void EnsureRuleScope(ItemRule rule, int sourceId, ContentType contentType)
    {
        if (rule.XtreamSourceId != sourceId || rule.ContentType != contentType)
        {
            throw new InvalidOperationException("The item rule does not belong to the selected source or content type.");
        }
    }

    private static async Task<IReadOnlyList<ItemRuleDefinition>> LoadRuleDefinitionsAsync(
        XtreamForgeDbContext dbContext,
        int sourceId,
        ContentType contentType,
        CancellationToken cancellationToken)
    {
        return await dbContext.ItemRules
            .Where(rule => rule.XtreamSourceId == sourceId && rule.ContentType == contentType)
            .OrderBy(rule => rule.Sequence)
            .ThenBy(rule => rule.Id)
            .Select(rule => new ItemRuleDefinition(
                rule.Id,
                rule.Sequence,
                rule.Field,
                rule.Action,
                rule.Operator,
                rule.Pattern,
                rule.CaseSensitive,
                rule.IsEnabled))
            .ToListAsync(cancellationToken);
    }

    private static async Task ReassignSequencesAsync(
        XtreamForgeDbContext dbContext,
        List<ItemRule> orderedRules,
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

public sealed record ItemRuleSummary(
    int Id,
    int Sequence,
    ItemRuleField Field,
    ItemRuleAction Action,
    ItemRuleOperator Operator,
    string Pattern,
    bool CaseSensitive,
    bool IsEnabled);

public sealed record ItemRulesAdministrationView(
    IReadOnlyList<XtreamSourceSummary> Sources,
    int? SelectedSourceId,
    ContentType SelectedContentType,
    IReadOnlyList<ItemRuleSummary> Rules);

public sealed record ItemRulePreviewResult(
    string ItemName,
    ItemRuleEvaluationResult Evaluation);

public sealed record ItemRuleEditorCommand(
    int? RuleId,
    int SelectedSourceId,
    ContentType SelectedContentType,
    ItemRuleField Field,
    ItemRuleAction Action,
    ItemRuleOperator Operator,
    string? Pattern,
    bool CaseSensitive,
    bool IsEnabled);

public record ItemRuleIdentityCommand(
    int RuleId,
    int SelectedSourceId,
    ContentType SelectedContentType);

public sealed record ItemRuleOrderCommand(
    int SelectedSourceId,
    ContentType SelectedContentType,
    IReadOnlyList<int> OrderedRuleIds);

public sealed record ItemRuleDeleteCommand(
    int RuleId,
    int SelectedSourceId,
    ContentType SelectedContentType,
    bool ConfirmDelete) : ItemRuleIdentityCommand(RuleId, SelectedSourceId, SelectedContentType);

public sealed record ItemRuleMutationResult(
    int SourceId,
    ContentType ContentType);

public sealed record ItemRuleSet(
    int? SourceId,
    IReadOnlyList<ItemRuleDefinition> Rules);
