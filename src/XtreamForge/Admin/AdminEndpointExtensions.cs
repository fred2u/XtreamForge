using Microsoft.EntityFrameworkCore;
using XtreamForge.Categories;
using XtreamForge.Data;

namespace XtreamForge.Admin;

public static class AdminEndpointExtensions
{
    public static IEndpointRouteBuilder MapAdminEndpoints(this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        var adminGroup = endpoints.MapGroup("/api/admin");

        adminGroup.MapGet("/status", GetStatusAsync)
            .WithName("GetAdminStatus")
            .WithSummary("Gets the administration dashboard status.");

        adminGroup.MapGet("/categories", GetCategoriesAsync)
            .WithName("GetAdminCategories")
            .WithSummary("Gets source categories and custom categories for the administration UI.");

        adminGroup.MapPut("/categories/{upstreamCategoryRecordId:int}/mapping", UpdateCategoryMappingAsync)
            .WithName("UpdateAdminCategoryMapping")
            .WithSummary("Updates the effective mapping for one source category.");

        adminGroup.MapGet("/category-rules", GetCategoryRulesAsync)
            .WithName("GetAdminCategoryRules")
            .WithSummary("Gets category rules for the selected source and content type.");

        adminGroup.MapPost("/category-rules", CreateCategoryRuleAsync)
            .WithName("CreateAdminCategoryRule")
            .WithSummary("Creates a category rule.");

        adminGroup.MapPut("/category-rules/{ruleId:int}", UpdateCategoryRuleAsync)
            .WithName("UpdateAdminCategoryRule")
            .WithSummary("Updates a category rule.");

        adminGroup.MapPost("/category-rules/{ruleId:int}/move-up", MoveCategoryRuleUpAsync)
            .WithName("MoveAdminCategoryRuleUp")
            .WithSummary("Moves a category rule up.");

        adminGroup.MapPost("/category-rules/{ruleId:int}/move-down", MoveCategoryRuleDownAsync)
            .WithName("MoveAdminCategoryRuleDown")
            .WithSummary("Moves a category rule down.");

        adminGroup.MapDelete("/category-rules/{ruleId:int}", DeleteCategoryRuleAsync)
            .WithName("DeleteAdminCategoryRule")
            .WithSummary("Deletes a category rule.");

        adminGroup.MapPost("/custom-categories", CreateCustomCategoryAsync)
            .WithName("CreateAdminCustomCategory")
            .WithSummary("Creates a global custom category.");

        adminGroup.MapPut("/custom-categories/{customCategoryId:int}", UpdateCustomCategoryAsync)
            .WithName("UpdateAdminCustomCategory")
            .WithSummary("Renames a global custom category.");

        adminGroup.MapDelete("/custom-categories/{customCategoryId:int}", DeleteCustomCategoryAsync)
            .WithName("DeleteAdminCustomCategory")
            .WithSummary("Deletes a global custom category when it is no longer referenced.");

        return endpoints;
    }

    private static async Task<IResult> GetStatusAsync(
        IDbContextFactory<XtreamForgeDbContext> dbContextFactory,
        CancellationToken cancellationToken)
    {
        var databaseStatus = "Unavailable";
        var databaseDetails = "The database connectivity check failed.";

        try
        {
            await using var dbContext = await dbContextFactory.CreateDbContextAsync(cancellationToken);
            var canConnect = await dbContext.Database.CanConnectAsync(cancellationToken);
            var providerDisplayName = GetProviderDisplayName(dbContext.Database.ProviderName);

            databaseStatus = canConnect ? "Connected" : "Unavailable";
            databaseDetails = canConnect
                ? $"{providerDisplayName} is reachable."
                : $"{providerDisplayName} is not reachable.";
        }
        catch
        {
        }

        return TypedResults.Ok(new AdminStatusResponse(
            ApplicationName: "XtreamForge",
            ApplicationVersion: typeof(Program).Assembly.GetName().Version?.ToString() ?? "unknown",
            Status: "Healthy",
            DatabaseStatus: databaseStatus,
            DatabaseDetails: databaseDetails));
    }

    private static async Task<IResult> GetCategoriesAsync(
        int? sourceId,
        string? contentType,
        XtreamCategoryMappingService categoryMappingService,
        CancellationToken cancellationToken)
    {
        try
        {
            var selectedContentType = ParseContentType(contentType);
            var view = await categoryMappingService.GetAdministrationViewAsync(sourceId, selectedContentType, cancellationToken);
            return TypedResults.Ok(new AdminCategoriesResponse(
                view.Sources.Select(ToResponse).ToList(),
                view.SelectedSourceId,
                view.SelectedContentType.ToString(),
                view.CustomCategories.Select(ToResponse).ToList(),
                view.UpstreamCategories.Select(ToResponse).ToList()));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return TypedResults.BadRequest(new AdminErrorResponse(exception.Message));
        }
    }

    private static async Task<IResult> UpdateCategoryMappingAsync(
        int upstreamCategoryRecordId,
        AdminCategoryMappingRequest request,
        XtreamCategoryMappingService categoryMappingService,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await categoryMappingService.SaveCategoryConfigurationAsync(
                new CategoryConfigurationCommand(
                    upstreamCategoryRecordId,
                    request.SelectedSourceId,
                    ParseContentType(request.SelectedContentType),
                    ParseMappingSelection(request.MappingSelection),
                    request.CustomCategoryId,
                    request.NewCustomCategoryName),
                cancellationToken);

            return TypedResults.Ok(new AdminMutationResponse(result.SourceId, result.ContentType.ToString()));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return TypedResults.BadRequest(new AdminErrorResponse(exception.Message));
        }
    }

    private static async Task<IResult> GetCategoryRulesAsync(
        int? sourceId,
        string? contentType,
        CategoryRuleService categoryRuleService,
        CancellationToken cancellationToken)
    {
        try
        {
            var selectedContentType = ParseContentType(contentType);
            if (sourceId is null)
            {
                return TypedResults.Ok(new AdminCategoryRulesResponse(null, selectedContentType.ToString(), []));
            }

            var rules = await categoryRuleService.GetRuleDefinitionsAsync(sourceId.Value, selectedContentType, cancellationToken);
            return TypedResults.Ok(new AdminCategoryRulesResponse(
                sourceId,
                selectedContentType.ToString(),
                rules.Select(ToResponse).ToList()));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return TypedResults.BadRequest(new AdminErrorResponse(exception.Message));
        }
    }

    private static async Task<IResult> CreateCategoryRuleAsync(
        AdminCategoryRuleRequest request,
        CategoryRuleService categoryRuleService,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await categoryRuleService.CreateRuleAsync(ToRuleEditorCommand(null, request), cancellationToken);
            return TypedResults.Ok(new AdminMutationResponse(result.SourceId, result.ContentType.ToString()));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return TypedResults.BadRequest(new AdminErrorResponse(exception.Message));
        }
    }

    private static async Task<IResult> UpdateCategoryRuleAsync(
        int ruleId,
        AdminCategoryRuleRequest request,
        CategoryRuleService categoryRuleService,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await categoryRuleService.UpdateRuleAsync(ToRuleEditorCommand(ruleId, request), cancellationToken);
            return TypedResults.Ok(new AdminMutationResponse(result.SourceId, result.ContentType.ToString()));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return TypedResults.BadRequest(new AdminErrorResponse(exception.Message));
        }
    }

    private static Task<IResult> MoveCategoryRuleUpAsync(
        int ruleId,
        int sourceId,
        string contentType,
        CategoryRuleService categoryRuleService,
        CancellationToken cancellationToken) =>
        MoveCategoryRuleAsync(ruleId, sourceId, contentType, CategoryRuleMoveDirection.Up, categoryRuleService, cancellationToken);

    private static Task<IResult> MoveCategoryRuleDownAsync(
        int ruleId,
        int sourceId,
        string contentType,
        CategoryRuleService categoryRuleService,
        CancellationToken cancellationToken) =>
        MoveCategoryRuleAsync(ruleId, sourceId, contentType, CategoryRuleMoveDirection.Down, categoryRuleService, cancellationToken);

    private static async Task<IResult> MoveCategoryRuleAsync(
        int ruleId,
        int sourceId,
        string contentType,
        CategoryRuleMoveDirection direction,
        CategoryRuleService categoryRuleService,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await categoryRuleService.MoveRuleAsync(
                new CategoryRuleIdentityCommand(ruleId, sourceId, ParseContentType(contentType)),
                direction,
                cancellationToken);

            return TypedResults.Ok(new AdminMutationResponse(result.SourceId, result.ContentType.ToString()));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return TypedResults.BadRequest(new AdminErrorResponse(exception.Message));
        }
    }

    private static async Task<IResult> DeleteCategoryRuleAsync(
        int ruleId,
        int sourceId,
        string contentType,
        bool confirmDelete,
        CategoryRuleService categoryRuleService,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await categoryRuleService.DeleteRuleAsync(
                new CategoryRuleDeleteCommand(ruleId, sourceId, ParseContentType(contentType), confirmDelete),
                cancellationToken);

            return TypedResults.Ok(new AdminMutationResponse(result.SourceId, result.ContentType.ToString()));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return TypedResults.BadRequest(new AdminErrorResponse(exception.Message));
        }
    }

    private static async Task<IResult> CreateCustomCategoryAsync(
        AdminCustomCategoryCreateRequest request,
        XtreamCategoryMappingService categoryMappingService,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await categoryMappingService.CreateCustomCategoryAsync(
                new CustomCategoryCreateCommand(ParseContentType(request.SelectedContentType), request.DisplayName),
                cancellationToken);

            return TypedResults.Ok(ToResponse(result));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return TypedResults.BadRequest(new AdminErrorResponse(exception.Message));
        }
    }

    private static async Task<IResult> UpdateCustomCategoryAsync(
        int customCategoryId,
        AdminCustomCategoryUpdateRequest request,
        XtreamCategoryMappingService categoryMappingService,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await categoryMappingService.UpdateCustomCategoryAsync(
                new CustomCategoryUpdateCommand(customCategoryId, ParseContentType(request.SelectedContentType), request.DisplayName),
                cancellationToken);

            return TypedResults.Ok(new AdminContentTypeMutationResponse(result.ContentType.ToString()));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return TypedResults.BadRequest(new AdminErrorResponse(exception.Message));
        }
    }

    private static async Task<IResult> DeleteCustomCategoryAsync(
        int customCategoryId,
        string contentType,
        XtreamCategoryMappingService categoryMappingService,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await categoryMappingService.DeleteCustomCategoryAsync(
                new CustomCategoryDeleteCommand(customCategoryId, ParseContentType(contentType)),
                cancellationToken);

            return TypedResults.Ok(new AdminContentTypeMutationResponse(result.ContentType.ToString()));
        }
        catch (Exception exception) when (exception is InvalidOperationException or ArgumentException)
        {
            return TypedResults.BadRequest(new AdminErrorResponse(exception.Message));
        }
    }

    private static CategoryRuleEditorCommand ToRuleEditorCommand(int? ruleId, AdminCategoryRuleRequest request) =>
        new(
            ruleId,
            request.SelectedSourceId,
            ParseContentType(request.SelectedContentType),
            ParseRuleAction(request.Action),
            ParseRuleOperator(request.Operator),
            request.Pattern,
            request.CaseSensitive,
            request.IsEnabled);

    private static AdminSourceResponse ToResponse(XtreamSourceSummary source) =>
        new(source.Id, source.Protocol, source.Host, source.Port, source.LastSeenAtUtc);

    private static AdminCustomCategoryResponse ToResponse(CustomCategorySummary category) =>
        new(category.Id, category.XtreamForgeCategoryId, category.DisplayName, category.UsageCount);

    private static AdminUpstreamCategoryResponse ToResponse(UpstreamCategorySummary category) =>
        new(
            category.Id,
            category.UpstreamCategoryId,
            category.UpstreamCategoryName,
            category.IsManuallyExcluded,
            category.CustomCategoryId,
            category.CustomCategoryName,
            category.DedicatedXtreamForgeCategoryId,
            category.DedicatedOutputName,
            category.EffectiveDecision.ToString(),
            category.RuleDecision.ToString(),
            category.MatchedRuleId,
            category.MatchedRuleSequence,
            category.MatchedRuleAction?.ToString(),
            category.MatchedRuleOperator?.ToString(),
            category.MatchedPattern,
            category.MatchedRuleCaseSensitive,
            category.IsEffectivelyIncluded,
            category.CurrentMappingSelection.ToString());

    private static AdminCategoryRuleResponse ToResponse(CategoryRuleDefinition rule) =>
        new(
            rule.Id ?? 0,
            rule.Sequence,
            rule.Action.ToString(),
            rule.Operator.ToString(),
            rule.Pattern,
            rule.CaseSensitive,
            rule.IsEnabled);

    private static ContentType ParseContentType(string? value)
    {
        if (Enum.TryParse<ContentType>(value, true, out var contentType))
        {
            return contentType;
        }

        throw new InvalidOperationException("Content type must be 'Vod' or 'Series'.");
    }

    private static CategoryMappingSelection ParseMappingSelection(string? value)
    {
        if (Enum.TryParse<CategoryMappingSelection>(value, true, out var mappingSelection))
        {
            return mappingSelection;
        }

        throw new InvalidOperationException("Mapping selection must be 'Disabled', 'Original', or 'Custom'.");
    }

    private static CategoryRuleAction ParseRuleAction(string? value)
    {
        if (Enum.TryParse<CategoryRuleAction>(value, true, out var action))
        {
            return action;
        }

        throw new InvalidOperationException("Rule action must be 'Include' or 'Exclude'.");
    }

    private static CategoryRuleOperator ParseRuleOperator(string? value)
    {
        if (Enum.TryParse<CategoryRuleOperator>(value, true, out var categoryRuleOperator))
        {
            return categoryRuleOperator;
        }

        throw new InvalidOperationException("Rule operator must be 'Contains' or 'StartsWith'.");
    }

    private static string GetProviderDisplayName(string? providerName) => providerName?.Contains("Npgsql", StringComparison.OrdinalIgnoreCase) == true
        ? "PostgreSQL"
        : providerName?.Contains("Sqlite", StringComparison.OrdinalIgnoreCase) == true
            ? "SQLite"
            : "The configured database";
}

public sealed record AdminStatusResponse(
    string ApplicationName,
    string ApplicationVersion,
    string Status,
    string DatabaseStatus,
    string DatabaseDetails);

public sealed record AdminCategoriesResponse(
    IReadOnlyList<AdminSourceResponse> Sources,
    int? SelectedSourceId,
    string SelectedContentType,
    IReadOnlyList<AdminCustomCategoryResponse> CustomCategories,
    IReadOnlyList<AdminUpstreamCategoryResponse> UpstreamCategories);

public sealed record AdminSourceResponse(int Id, string Protocol, string Host, int Port, DateTimeOffset LastSeenAtUtc);

public sealed record AdminCustomCategoryResponse(int Id, int XtreamForgeCategoryId, string DisplayName, int UsageCount);

public sealed record AdminUpstreamCategoryResponse(
    int Id,
    string UpstreamCategoryId,
    string UpstreamCategoryName,
    bool IsManuallyExcluded,
    int? CustomCategoryId,
    string? CustomCategoryName,
    int DedicatedXtreamForgeCategoryId,
    string DedicatedOutputName,
    string EffectiveDecision,
    string RuleDecision,
    int? MatchedRuleId,
    int? MatchedRuleSequence,
    string? MatchedRuleAction,
    string? MatchedRuleOperator,
    string? MatchedPattern,
    bool? MatchedRuleCaseSensitive,
    bool IsEffectivelyIncluded,
    string CurrentMappingSelection);

public sealed record AdminCategoryMappingRequest(
    int SelectedSourceId,
    string SelectedContentType,
    string MappingSelection,
    int? CustomCategoryId,
    string? NewCustomCategoryName);

public sealed record AdminCategoryRulesResponse(
    int? SelectedSourceId,
    string SelectedContentType,
    IReadOnlyList<AdminCategoryRuleResponse> Rules);

public sealed record AdminCategoryRuleResponse(
    int Id,
    int Sequence,
    string Action,
    string Operator,
    string Pattern,
    bool CaseSensitive,
    bool IsEnabled);

public sealed record AdminCategoryRuleRequest(
    int SelectedSourceId,
    string SelectedContentType,
    string Action,
    string Operator,
    string? Pattern,
    bool CaseSensitive,
    bool IsEnabled);

public sealed record AdminCustomCategoryCreateRequest(string SelectedContentType, string? DisplayName);

public sealed record AdminCustomCategoryUpdateRequest(string SelectedContentType, string DisplayName);

public sealed record AdminMutationResponse(int SourceId, string ContentType);

public sealed record AdminContentTypeMutationResponse(string ContentType);

public sealed record AdminErrorResponse(string Message);
