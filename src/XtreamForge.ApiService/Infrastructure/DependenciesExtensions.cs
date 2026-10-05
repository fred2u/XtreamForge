using XtreamForge.ApiService.Endpoints.Xtream;
using XtreamForge.ApiService.Services;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.ApiService.Services.Monitoring;
using XtreamForge.ApiService.Services.Tmdb;
using XtreamForge.ApiService.Services.Tmdb.Scoring;
using XtreamForge.ApiService.Xtream;
using XtreamForge.Domain.Categories;
using XtreamForge.Domain.Items;

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
        services.AddScoped<RecommendationService>();
        services.AddScoped<PopularService>();
        services.AddScoped<VirtualCategoryService>();
        services.AddSingleton<TmdbIdCache>();

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
        // the admin endpoints are static handlers (Endpoints/Admin): they receive these services as parameters
        services.AddScoped<SourceAdminService>();
        services.AddScoped<XtreamCategoryDiscoveryAdminService>();
        services.AddScoped<XtreamCategoryAdminService>();
        services.AddScoped<CustomCategoryAdminService>();
        services.AddScoped<SourceRuleAdminService<CategoryRule>>();
        services.AddScoped<SourceRuleAdminService<ItemRule>>();
        services.AddScoped<TmdbRuleAdminService>();
        services.AddScoped<TmdbInfoAdminService>();
        services.AddScoped<StreamTmdbMappingAdminService>();
        services.AddScoped<WatchHistoryAdminService>();
    }

    private static void AddHostedServices(IServiceCollection services)
    {
        services.AddHostedService<TmdbIdRetrieverBackgroundService>();
        services.AddHostedService<TmdbInfoBackgroundService>();
        services.AddHostedService<WatchHistoryBackgroundService>();
        services.AddHostedService<QueueMonitorSamplingService>();
    }
}
