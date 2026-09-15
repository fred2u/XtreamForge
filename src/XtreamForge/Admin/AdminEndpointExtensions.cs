using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using XtreamForge.Categories;
using XtreamForge.Data;
using XtreamForge.Xtream;

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

        adminGroup.MapPost("/sources/discover", DiscoverSourceAsync)
            .WithName("DiscoverAdminSource")
            .WithSummary("Discovers or refreshes an Xtream source using transient credentials.");

        adminGroup.MapGet("/category-rules", GetCategoryRulesAsync)
            .WithName("GetAdminCategoryRules")
            .WithSummary("Gets category rules for the selected source and content type.");

        adminGroup.MapGet("/category-rules/preview", PreviewCategoryRulesAsync)
            .WithName("PreviewAdminCategoryRules")
            .WithSummary("Previews the effective rule decision for a category name.");

        adminGroup.MapPost("/category-rules", CreateCategoryRuleAsync)
            .WithName("CreateAdminCategoryRule")
            .WithSummary("Creates a category rule.");

        adminGroup.MapPut("/category-rules/{ruleId:int}", UpdateCategoryRuleAsync)
            .WithName("UpdateAdminCategoryRule")
            .WithSummary("Updates a category rule.");

        adminGroup.MapPut("/category-rules/order", ReorderCategoryRulesAsync)
            .WithName("ReorderAdminCategoryRules")
            .WithSummary("Reorders category rules for the selected source and content type.");

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

        adminGroup.MapGet("/custom-categories/{customCategoryId:int}/usages", GetCustomCategoryUsagesAsync)
            .WithName("GetAdminCustomCategoryUsages")
            .WithSummary("Gets source category usages for a global custom category.");

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

    private static async Task<IResult> DiscoverSourceAsync(
        AdminSourceDiscoveryRequest request,
        XtreamSourceDiscoveryService sourceDiscoveryService,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await sourceDiscoveryService.DiscoverAsync(
                new XtreamSourceDiscoveryRequest(
                    request.Protocol,
                    request.HostOrBaseUrl,
                    request.Port,
                    request.Username,
                    request.Password),
                cancellationToken);

            return TypedResults.Ok(new AdminSourceDiscoveryResponse(result.SourceId, result.VodCategoryCount, result.SeriesCategoryCount));
        }
        catch (HttpRequestException)
        {
            return TypedResults.BadRequest(new AdminErrorResponse("Connecting to the Xtream source failed."));
        }
        catch (TaskCanceledException)
        {
            return TypedResults.BadRequest(new AdminErrorResponse("Connecting to the Xtream source timed out."));
        }
        catch (System.Text.Json.JsonException)
        {
            return TypedResults.BadRequest(new AdminErrorResponse("The Xtream source returned an invalid category payload."));
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

    private static async Task<IResult> PreviewCategoryRulesAsync(
        int? sourceId,
        string? contentType,
        string? categoryName,
        CategoryRuleService categoryRuleService,
        CancellationToken cancellationToken)
    {
        try
        {
            if (sourceId is null)
            {
                return TypedResults.BadRequest(new AdminErrorResponse("A source must be selected."));
            }

            var preview = await categoryRuleService.PreviewAsync(sourceId, ParseContentType(contentType), categoryName, cancellationToken);
            if (preview is null)
            {
                return TypedResults.BadRequest(new AdminErrorResponse("A category name is required."));
            }

            return TypedResults.Ok(new AdminCategoryRulePreviewResponse(
                preview.CategoryName,
                preview.Evaluation.Decision.ToString(),
                preview.Evaluation.MatchedRuleId,
                preview.Evaluation.MatchedRuleSequence,
                preview.Evaluation.MatchedRuleAction?.ToString(),
                preview.Evaluation.MatchedRuleOperator?.ToString(),
                preview.Evaluation.MatchedPattern,
                preview.Evaluation.MatchedRuleCaseSensitive));
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

    private static async Task<IResult> ReorderCategoryRulesAsync(
        AdminCategoryRuleOrderRequest request,
        CategoryRuleService categoryRuleService,
        CancellationToken cancellationToken)
    {
        try
        {
            var result = await categoryRuleService.ReorderRulesAsync(
                new CategoryRuleOrderCommand(
                    request.SelectedSourceId,
                    ParseContentType(request.SelectedContentType),
                    request.OrderedRuleIds),
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

    private static async Task<IResult> GetCustomCategoryUsagesAsync(
        int customCategoryId,
        string contentType,
        XtreamCategoryMappingService categoryMappingService,
        CancellationToken cancellationToken)
    {
        try
        {
            var usages = await categoryMappingService.GetCustomCategoryUsagesAsync(
                customCategoryId,
                ParseContentType(contentType),
                cancellationToken);

            return TypedResults.Ok(usages.Select(usage => new AdminCustomCategoryUsageResponse(
                usage.UpstreamCategoryRecordId,
                usage.SourceId,
                usage.SourceProtocol,
                usage.SourceHost,
                usage.SourcePort,
                usage.ContentType.ToString(),
                usage.UpstreamCategoryId,
                usage.UpstreamCategoryName)).ToList());
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

public sealed record AdminCategoryRulePreviewResponse(
    string CategoryName,
    string Decision,
    int? MatchedRuleId,
    int? MatchedRuleSequence,
    string? MatchedRuleAction,
    string? MatchedRuleOperator,
    string? MatchedPattern,
    bool? MatchedRuleCaseSensitive);

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

public sealed record AdminCategoryRuleOrderRequest(
    int SelectedSourceId,
    string SelectedContentType,
    IReadOnlyList<int> OrderedRuleIds);

public sealed record AdminCustomCategoryCreateRequest(string SelectedContentType, string? DisplayName);

public sealed record AdminCustomCategoryUpdateRequest(string SelectedContentType, string DisplayName);

public sealed record AdminSourceDiscoveryRequest(
    string? Protocol,
    string? HostOrBaseUrl,
    int? Port,
    string? Username,
    string? Password);

public sealed record AdminSourceDiscoveryResponse(
    int SourceId,
    int VodCategoryCount,
    int SeriesCategoryCount);

public sealed record AdminCustomCategoryUsageResponse(
    int UpstreamCategoryRecordId,
    int SourceId,
    string SourceProtocol,
    string SourceHost,
    int SourcePort,
    string ContentType,
    string UpstreamCategoryId,
    string UpstreamCategoryName);

public sealed record AdminMutationResponse(int SourceId, string ContentType);

public sealed record AdminContentTypeMutationResponse(string ContentType);

public sealed record AdminErrorResponse(string Message);
