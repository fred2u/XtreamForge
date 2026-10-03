using XtreamForge.ApiService.Endpoints.Admin.CategoryRules;
using XtreamForge.ApiService.Endpoints.Admin.CategoryRules.Dto;
using XtreamForge.ApiService.Endpoints.Admin.CustomCategories;
using XtreamForge.ApiService.Endpoints.Admin.CustomCategories.Dto;
using XtreamForge.ApiService.Endpoints.Admin.Dashboard;
using XtreamForge.ApiService.Endpoints.Admin.ItemRules;
using XtreamForge.ApiService.Endpoints.Admin.ItemRules.Dto;
using XtreamForge.ApiService.Endpoints.Admin.Monitoring;
using XtreamForge.ApiService.Endpoints.Admin.Recommendations;
using XtreamForge.ApiService.Endpoints.Admin.Sources;
using XtreamForge.ApiService.Endpoints.Admin.Sources.Dto;
using XtreamForge.ApiService.Endpoints.Admin.StreamTmdbMappings;
using XtreamForge.ApiService.Endpoints.Admin.StreamTmdbMappings.Dto;
using XtreamForge.ApiService.Endpoints.Admin.TmdbInfos;
using XtreamForge.ApiService.Endpoints.Admin.TmdbInfos.Dto;
using XtreamForge.ApiService.Endpoints.Admin.TmdbRules;
using XtreamForge.ApiService.Endpoints.Admin.TmdbRules.Dto;
using XtreamForge.ApiService.Endpoints.Admin.WatchHistory;
using XtreamForge.ApiService.Endpoints.Admin.XtreamCategories;
using XtreamForge.ApiService.Endpoints.Admin.XtreamCategories.Dto;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.Domain.Enums;

namespace XtreamForge.ApiService.Endpoints.Admin;

public static class RouteExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        public IEndpointRouteBuilder MapAdminEndpoints()
        {
            // api/admin/status
            endpoints.MapGet("/api/admin/status", async (DashboardStatusGetEndpoint dashboardStatusEndpoint, CancellationToken cancellationToken) => await dashboardStatusEndpoint.GetAsync(cancellationToken));

            // api/admin/queues: size and one hour history of the background queues, rate-limited upstream hosts
            endpoints.MapGet("/api/admin/queues", (QueuesGetEndpoint endpoint) => endpoint.Get());

            // api/admin/custom-categories?contentType=
            endpoints.MapGet("/api/admin/custom-categories", async (ContentType contentType, CustomCategoriesGetEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.GetAsync(contentType, cancellationToken));
            endpoints.MapPost("/api/admin/custom-categories", async (AdminCustomCategoryRequest request, CustomCategoryPostEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.PostAsync(request, cancellationToken));
            endpoints.MapPut("/api/admin/custom-categories/{id:int}", async (int id, AdminCustomCategoryRequest request, CustomCategoryPutEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.PutAsync(id, request, cancellationToken));
            endpoints.MapDelete("/api/admin/custom-categories/{id:int}", async (int id, CustomCategoryDeleteEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.DeleteAsync(id, cancellationToken));

            // api/admin/sources
            endpoints.MapGet("/api/admin/sources", async (SourcesGetEndpoint sourcesGetEndpoint, CancellationToken cancellationToken) => await sourcesGetEndpoint.GetAsync(cancellationToken));
            endpoints.MapPost("/api/admin/sources", async (XtreamSourceCreateRequest request, SourcesPostEndpoint sourcesPostEndpoint, CancellationToken cancellationToken) => await sourcesPostEndpoint.PostAsync(request, cancellationToken));
            endpoints.MapDelete("/api/admin/sources/{id:int}", async (int id, SourcesDeleteEndpoint sourcesDeleteEndpoint, CancellationToken cancellationToken) => await sourcesDeleteEndpoint.DeleteAsync(id, cancellationToken));

            // api/admin/sources/{sourceId}/xtream-categories?contentType=
            endpoints.MapGet("/api/admin/sources/{sourceId:int}/xtream-categories", async (int sourceId, ContentType contentType, XtreamCategoriesGetEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.GetAsync(sourceId, contentType, cancellationToken));
            endpoints.MapPatch("/api/admin/xtream-categories/{id:int}", async (int id, AdminXtreamCategoryPatchRequest request, XtreamCategoryPatchEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.PatchAsync(id, request, cancellationToken));

            // api/admin/sources/{sourceId}/category-rules?contentType=
            endpoints.MapGet("/api/admin/sources/{sourceId:int}/category-rules", async (int sourceId, ContentType contentType, CategoryRulesGetEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.GetAsync(sourceId, contentType, cancellationToken));
            endpoints.MapPost("/api/admin/sources/{sourceId:int}/category-rules", async (int sourceId, AdminCategoryRuleRequest request, CategoryRulePostEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.PostAsync(sourceId, request, cancellationToken));
            endpoints.MapPut("/api/admin/sources/{sourceId:int}/category-rules/order", async (int sourceId, AdminCategoryRuleOrderRequest request, CategoryRulesOrderPutEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.PutAsync(sourceId, request, cancellationToken));
            endpoints.MapPut("/api/admin/category-rules/{id:int}", async (int id, AdminCategoryRuleRequest request, CategoryRulePutEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.PutAsync(id, request, cancellationToken));
            endpoints.MapDelete("/api/admin/category-rules/{id:int}", async (int id, CategoryRuleDeleteEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.DeleteAsync(id, cancellationToken));

            // api/admin/sources/{sourceId}/item-rules?contentType=
            endpoints.MapGet("/api/admin/sources/{sourceId:int}/item-rules", async (int sourceId, ContentType contentType, ItemRulesGetEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.GetAsync(sourceId, contentType, cancellationToken));
            endpoints.MapPost("/api/admin/sources/{sourceId:int}/item-rules", async (int sourceId, AdminItemRuleRequest request, ItemRulePostEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.PostAsync(sourceId, request, cancellationToken));
            endpoints.MapPut("/api/admin/sources/{sourceId:int}/item-rules/order", async (int sourceId, AdminItemRuleOrderRequest request, ItemRulesOrderPutEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.PutAsync(sourceId, request, cancellationToken));
            endpoints.MapPut("/api/admin/item-rules/{id:int}", async (int id, AdminItemRuleRequest request, ItemRulePutEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.PutAsync(id, request, cancellationToken));
            endpoints.MapDelete("/api/admin/item-rules/{id:int}", async (int id, ItemRuleDeleteEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.DeleteAsync(id, cancellationToken));

            // api/admin/tmdb-infos?contentType=&search=&genre=&decision=&isExcluded=&isLoaded=&skip=&take=
            endpoints.MapGet("/api/admin/tmdb-infos", async ([AsParameters] TmdbInfoListQuery query, TmdbInfosGetEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.GetAsync(query, cancellationToken));
            endpoints.MapGet("/api/admin/tmdb-infos/{id:int}", async (int id, TmdbInfoGetEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.GetAsync(id, cancellationToken));
            endpoints.MapPatch("/api/admin/tmdb-infos/{id:int}", async (int id, AdminTmdbInfoPatchRequest request, TmdbInfoPatchEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.PatchAsync(id, request, cancellationToken));

            // api/admin/sources/{sourceId}/tmdb-mappings?contentType=&search=&isMapped=&skip=&take=
            endpoints.MapGet("/api/admin/sources/{sourceId:int}/tmdb-mappings", async (int sourceId, [AsParameters] StreamTmdbMappingListQuery query, StreamTmdbMappingsGetEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.GetAsync(sourceId, query, cancellationToken));
            endpoints.MapPatch("/api/admin/tmdb-mappings/{id:int}", async (int id, AdminStreamTmdbMappingPatchRequest request, StreamTmdbMappingPatchEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.PatchAsync(id, request, cancellationToken));

            // api/admin/tmdb-rules?contentType= (global per content type)
            endpoints.MapGet("/api/admin/tmdb-rules", async (ContentType contentType, TmdbRulesGetEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.GetAsync(contentType, cancellationToken));
            endpoints.MapPost("/api/admin/tmdb-rules", async (AdminTmdbRuleRequest request, TmdbRulePostEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.PostAsync(request, cancellationToken));
            endpoints.MapPut("/api/admin/tmdb-rules/order", async (AdminTmdbRuleOrderRequest request, TmdbRulesOrderPutEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.PutAsync(request, cancellationToken));
            endpoints.MapPut("/api/admin/tmdb-rules/{id:int}", async (int id, AdminTmdbRuleRequest request, TmdbRulePutEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.PutAsync(id, request, cancellationToken));
            endpoints.MapDelete("/api/admin/tmdb-rules/{id:int}", async (int id, TmdbRuleDeleteEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.DeleteAsync(id, cancellationToken));

            // api/admin/watch-history?contentType=&skip=&take= (the most recent playback first)
            endpoints.MapGet("/api/admin/watch-history", async ([AsParameters] WatchHistoryListQuery query, WatchHistoryGetEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.GetAsync(query, cancellationToken));
            endpoints.MapDelete("/api/admin/watch-history/{id:int}", async (int id, WatchHistoryDeleteEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.DeleteAsync(id, cancellationToken));

            // api/admin/watch-history/activity?timeZone=: movie playbacks per day over the last 53 weeks
            endpoints.MapGet("/api/admin/watch-history/activity", async (string? timeZone, WatchHistoryActivityGetEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.GetAsync(timeZone, cancellationToken));

            // api/admin/tmdb-infos/{id}/watch-history: adds a playback of the entry, started now
            endpoints.MapPost("/api/admin/tmdb-infos/{id:int}/watch-history", async (int id, WatchHistoryPostEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.PostAsync(id, cancellationToken));

            // api/admin/recommendations: movies recommended by TMDB for the recently watched movies, never a watched one
            endpoints.MapGet("/api/admin/recommendations", async (RecommendationsGetEndpoint endpoint, CancellationToken cancellationToken) => await endpoint.GetAsync(cancellationToken));

            return endpoints;
        }
    }
}
