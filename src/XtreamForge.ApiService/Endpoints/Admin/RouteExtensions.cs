using XtreamForge.ApiService.Endpoints.Admin.CustomCategories;
using XtreamForge.ApiService.Endpoints.Admin.Dashboard;
using XtreamForge.ApiService.Endpoints.Admin.Monitoring;
using XtreamForge.ApiService.Endpoints.Admin.Recommendations;
using XtreamForge.ApiService.Endpoints.Admin.Rules;
using XtreamForge.ApiService.Endpoints.Admin.Sources;
using XtreamForge.ApiService.Endpoints.Admin.StreamTmdbMappings;
using XtreamForge.ApiService.Endpoints.Admin.TmdbInfos;
using XtreamForge.ApiService.Endpoints.Admin.WatchHistory;
using XtreamForge.ApiService.Endpoints.Admin.XtreamCategories;
using XtreamForge.Domain.Categories;
using XtreamForge.Domain.Items;
using XtreamForge.Domain.Rules;

namespace XtreamForge.ApiService.Endpoints.Admin;

public static class RouteExtensions
{
    extension(IEndpointRouteBuilder endpoints)
    {
        /// <summary>
        /// Maps the admin API. Handlers are static methods grouped per resource; their services are injected as parameters,
        /// so a route parameter must keep the name of the handler parameter it binds to.
        /// </summary>
        public IEndpointRouteBuilder MapAdminEndpoints()
        {
            // api/admin/status
            endpoints.MapGet("/api/admin/status", DashboardEndpoints.GetStatusAsync);

            // api/admin/queues: size and one hour history of the background queues, rate-limited upstream hosts
            endpoints.MapGet("/api/admin/queues", MonitoringEndpoints.GetQueues);

            // api/admin/custom-categories?contentType=
            endpoints.MapGet("/api/admin/custom-categories", CustomCategoryEndpoints.GetAsync);
            endpoints.MapPost("/api/admin/custom-categories", CustomCategoryEndpoints.PostAsync);
            endpoints.MapPut("/api/admin/custom-categories/{id:int}", CustomCategoryEndpoints.PutAsync);
            endpoints.MapDelete("/api/admin/custom-categories/{id:int}", CustomCategoryEndpoints.DeleteAsync);

            // api/admin/sources
            endpoints.MapGet("/api/admin/sources", SourceEndpoints.GetAsync);
            endpoints.MapPost("/api/admin/sources", SourceEndpoints.PostAsync);
            endpoints.MapDelete("/api/admin/sources/{id:int}", SourceEndpoints.DeleteAsync);

            // api/admin/sources/{sourceId}/xtream-categories?contentType=
            endpoints.MapGet("/api/admin/sources/{sourceId:int}/xtream-categories", XtreamCategoryEndpoints.GetAsync);
            endpoints.MapPatch("/api/admin/xtream-categories/{id:int}", XtreamCategoryEndpoints.PatchAsync);

            // api/admin/sources/{sourceId}/category-rules?contentType=
            MapSourceRules<CategoryRule>(endpoints, "category-rules");

            // api/admin/sources/{sourceId}/item-rules?contentType=
            MapSourceRules<ItemRule>(endpoints, "item-rules");

            // api/admin/tmdb-infos?contentType=&search=&genre=&decision=&isExcluded=&isLoaded=&skip=&take=
            endpoints.MapGet("/api/admin/tmdb-infos", TmdbInfoEndpoints.GetListAsync);
            endpoints.MapGet("/api/admin/tmdb-infos/{id:int}", TmdbInfoEndpoints.GetAsync);
            endpoints.MapPatch("/api/admin/tmdb-infos/{id:int}", TmdbInfoEndpoints.PatchAsync);

            // api/admin/sources/{sourceId}/tmdb-mappings?contentType=&search=&isMapped=&skip=&take=
            endpoints.MapGet("/api/admin/sources/{sourceId:int}/tmdb-mappings", StreamTmdbMappingEndpoints.GetListAsync);
            endpoints.MapPatch("/api/admin/tmdb-mappings/{id:int}", StreamTmdbMappingEndpoints.PatchAsync);

            // api/admin/tmdb-rules?contentType= (global per content type)
            endpoints.MapGet("/api/admin/tmdb-rules", TmdbRuleEndpoints.GetAsync);
            endpoints.MapPost("/api/admin/tmdb-rules", TmdbRuleEndpoints.PostAsync);
            endpoints.MapPut("/api/admin/tmdb-rules/order", TmdbRuleEndpoints.PutOrderAsync);
            endpoints.MapPut("/api/admin/tmdb-rules/{id:int}", TmdbRuleEndpoints.PutAsync);
            endpoints.MapDelete("/api/admin/tmdb-rules/{id:int}", TmdbRuleEndpoints.DeleteAsync);

            // api/admin/watch-history?contentType=&skip=&take= (the most recent playback first)
            endpoints.MapGet("/api/admin/watch-history", WatchHistoryEndpoints.GetListAsync);
            endpoints.MapDelete("/api/admin/watch-history/{id:int}", WatchHistoryEndpoints.DeleteAsync);

            // api/admin/watch-history/activity?timeZone=: movie playbacks per day over the last 53 weeks
            endpoints.MapGet("/api/admin/watch-history/activity", WatchHistoryEndpoints.GetActivityAsync);

            // api/admin/tmdb-infos/{tmdbInfoId}/watch-history: adds a playback of the entry, started now
            endpoints.MapPost("/api/admin/tmdb-infos/{tmdbInfoId:int}/watch-history", WatchHistoryEndpoints.PostAsync);

            // api/admin/recommendations: movies recommended by TMDB for the recently watched movies, never a watched one
            endpoints.MapGet("/api/admin/recommendations", RecommendationEndpoints.GetAsync);

            return endpoints;
        }
    }

    // the routes of the rules defined per source: /api/admin/sources/{sourceId}/{segment} and /api/admin/{segment}/{id}
    private static void MapSourceRules<TRule>(IEndpointRouteBuilder endpoints, string segment)
        where TRule : class, ISourceRule, new()
    {
        endpoints.MapGet($"/api/admin/sources/{{sourceId:int}}/{segment}", SourceRuleEndpoints.GetAsync<TRule>);
        endpoints.MapPost($"/api/admin/sources/{{sourceId:int}}/{segment}", SourceRuleEndpoints.PostAsync<TRule>);
        endpoints.MapPut($"/api/admin/sources/{{sourceId:int}}/{segment}/order", SourceRuleEndpoints.PutOrderAsync<TRule>);
        endpoints.MapPut($"/api/admin/{segment}/{{id:int}}", SourceRuleEndpoints.PutAsync<TRule>);
        endpoints.MapDelete($"/api/admin/{segment}/{{id:int}}", SourceRuleEndpoints.DeleteAsync<TRule>);
    }
}
