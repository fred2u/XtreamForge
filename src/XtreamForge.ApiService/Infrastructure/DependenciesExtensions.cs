using XtreamForge.ApiService.Endpoints.Admin.CategoryRules;
using XtreamForge.ApiService.Endpoints.Admin.CustomCategories;
using XtreamForge.ApiService.Endpoints.Admin.Dashboard;
using XtreamForge.ApiService.Endpoints.Admin.ItemRules;
using XtreamForge.ApiService.Endpoints.Admin.Monitoring;
using XtreamForge.ApiService.Endpoints.Admin.Sources;
using XtreamForge.ApiService.Endpoints.Admin.StreamTmdbMappings;
using XtreamForge.ApiService.Endpoints.Admin.TmdbInfos;
using XtreamForge.ApiService.Endpoints.Admin.TmdbRules;
using XtreamForge.ApiService.Endpoints.Admin.WatchHistory;
using XtreamForge.ApiService.Endpoints.Admin.XtreamCategories;
using XtreamForge.ApiService.Endpoints.Xtream;
using XtreamForge.ApiService.Services;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.ApiService.Services.Monitoring;
using XtreamForge.ApiService.Services.Tmdb;
using XtreamForge.ApiService.Services.Tmdb.Scoring;
using XtreamForge.ApiService.Xtream;

namespace XtreamForge.ApiService.Infrastructure;

public static class DependenciesExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddServices()
        {
            AddCoreServices(services);
            AddAdminServices(services);
            AddHostedServices(services);

            return services;
        }
    }

    private static void AddCoreServices(IServiceCollection services)
    {
        // infrastructure
        services.AddSingleton<XtreamContextBuilder>();
        services.AddSingleton<XtreamProviderValidator>();
        services.AddSingleton<XtreamAccountDirectory>();

        // services
        services.AddScoped<CategoryService>();
        services.AddScoped<ItemService>();
        services.AddScoped<SourceService>();
        services.AddScoped<TmdbIdRetrieverService>();
        services.AddSingleton<TmdbIdRetrieverQueue>();
        services.AddScoped<TmdbInfoService>();
        services.AddSingleton<TmdbInfoQueue>();
        services.AddScoped<WatchHistoryService>();
        services.AddSingleton<WatchHistoryQueue>();

        // monitoring: every background queue is also registered as IMonitoredQueue
        services.AddSingleton<IMonitoredQueue>(serviceProvider => serviceProvider.GetRequiredService<TmdbIdRetrieverQueue>());
        services.AddSingleton<IMonitoredQueue>(serviceProvider => serviceProvider.GetRequiredService<TmdbInfoQueue>());
        services.AddSingleton<IMonitoredQueue>(serviceProvider => serviceProvider.GetRequiredService<WatchHistoryQueue>());
        services.AddSingleton<QueueMonitor>();

        // tmdb
        services.AddSingleton<TmdbClient>();
        services.AddSingleton<TmdbIdMatcher>();
        services.AddSingleton<ITmdbScoringRule, TitleScoringRule>();
        services.AddSingleton<ITmdbScoringRule, PosterScoringRule>();
        services.AddSingleton<ITmdbScoringRule, ReleaseDateScoringRule>();
        services.AddSingleton<ITmdbScoringRule, CastScoringRule>();
        services.AddSingleton<ITmdbScoringRule, GenreScoringRule>();
        services.AddSingleton<ITmdbScoringRule, SingleCandidateScoringRule>();
        services.AddSingleton<ITmdbScoringRule, SeasonScoringRule>();

        // endpoints
        services.AddScoped<AuthenticateEndpoint>();
        services.AddScoped<CategoriesGetEndpoint>();
        services.AddScoped<ItemsGetEndpoint>();
        services.AddScoped<ItemGetEndpoint>();
        services.AddScoped<XtreamRequestForwardEndpoint>();
    }

    private static void AddAdminServices(IServiceCollection services)
    {
        // services
        services.AddScoped<SourceAdminService>();
        services.AddScoped<XtreamCategoryDiscoveryAdminService>();
        services.AddScoped<XtreamCategoryAdminService>();
        services.AddScoped<CustomCategoryAdminService>();
        services.AddScoped<CategoryRuleAdminService>();
        services.AddScoped<ItemRuleAdminService>();
        services.AddScoped<TmdbRuleAdminService>();
        services.AddScoped<TmdbInfoAdminService>();
        services.AddScoped<StreamTmdbMappingAdminService>();
        services.AddScoped<WatchHistoryAdminService>();

        // endpoints
        services.AddScoped<DashboardStatusGetEndpoint>();
        services.AddScoped<QueuesGetEndpoint>();
        services.AddScoped<SourcesGetEndpoint>();
        services.AddScoped<SourcesPostEndpoint>();
        services.AddScoped<SourcesDeleteEndpoint>();
        services.AddScoped<XtreamCategoriesGetEndpoint>();
        services.AddScoped<XtreamCategoryPatchEndpoint>();
        services.AddScoped<CustomCategoriesGetEndpoint>();
        services.AddScoped<CustomCategoryPostEndpoint>();
        services.AddScoped<CustomCategoryPutEndpoint>();
        services.AddScoped<CustomCategoryDeleteEndpoint>();
        services.AddScoped<CategoryRulesGetEndpoint>();
        services.AddScoped<CategoryRulePostEndpoint>();
        services.AddScoped<CategoryRulePutEndpoint>();
        services.AddScoped<CategoryRulesOrderPutEndpoint>();
        services.AddScoped<CategoryRuleDeleteEndpoint>();
        services.AddScoped<ItemRulesGetEndpoint>();
        services.AddScoped<ItemRulePostEndpoint>();
        services.AddScoped<ItemRulePutEndpoint>();
        services.AddScoped<ItemRulesOrderPutEndpoint>();
        services.AddScoped<ItemRuleDeleteEndpoint>();
        services.AddScoped<TmdbInfosGetEndpoint>();
        services.AddScoped<TmdbInfoGetEndpoint>();
        services.AddScoped<TmdbInfoPatchEndpoint>();
        services.AddScoped<StreamTmdbMappingsGetEndpoint>();
        services.AddScoped<StreamTmdbMappingPatchEndpoint>();
        services.AddScoped<TmdbRulesGetEndpoint>();
        services.AddScoped<TmdbRulePostEndpoint>();
        services.AddScoped<TmdbRulePutEndpoint>();
        services.AddScoped<TmdbRulesOrderPutEndpoint>();
        services.AddScoped<TmdbRuleDeleteEndpoint>();
        services.AddScoped<WatchHistoryGetEndpoint>();
    }

    private static void AddHostedServices(IServiceCollection services)
    {
        services.AddHostedService<TmdbIdRetrieverBackgroundService>();
        services.AddHostedService<TmdbInfoBackgroundService>();
        services.AddHostedService<WatchHistoryBackgroundService>();
        services.AddHostedService<QueueMonitorSamplingService>();
    }
}
