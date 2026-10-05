using XtreamForge.ApiService.Endpoints.Xtream;
using XtreamForge.ApiService.Services;
using XtreamForge.ApiService.Services.Admin;
using XtreamForge.ApiService.Services.Monitoring;
using XtreamForge.ApiService.Services.Queues;
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
        services.AddScoped<RecommendationService>();
        services.AddScoped<PopularService>();
        services.AddScoped<VirtualCategoryService>();
        services.AddSingleton<TmdbIdCache>();

        // background queues, in the order of the monitoring screen
        AddBackgroundQueue<TmdbIdRetrieverQueue, TmdbIdRetrieverRequest, TmdbIdRetrieverService>(services, workerCount: 2);
        AddBackgroundQueue<TmdbInfoQueue, TmdbInfoRequest, TmdbInfoService>(services);
        AddBackgroundQueue<WatchHistoryQueue, WatchHistoryRequest, WatchHistoryService>(services);
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
        services.AddHostedService<QueueMonitorSamplingService>();
    }

    // a background queue: the queue singleton, also monitored, and its consumer, which processes each request in its own scope
    // with TProcessor (also injectable as itself)
    private static void AddBackgroundQueue<TQueue, TRequest, TProcessor>(IServiceCollection services, int workerCount = 1)
        where TQueue : BackgroundQueue<TRequest>
        where TRequest : notnull
        where TProcessor : class, IQueueProcessor<TRequest>
    {
        services.AddSingleton<TQueue>();
        services.AddSingleton<IMonitoredQueue>(serviceProvider => serviceProvider.GetRequiredService<TQueue>());
        services.AddScoped<TProcessor>();
        services.AddHostedService(serviceProvider => new QueueBackgroundService<TRequest, TProcessor>(
            serviceProvider.GetRequiredService<TQueue>(),
            workerCount,
            serviceProvider.GetRequiredService<QueueMonitor>(),
            serviceProvider.GetRequiredService<IServiceScopeFactory>(),
            serviceProvider.GetRequiredService<ILogger<QueueBackgroundService<TRequest, TProcessor>>>()));
    }
}
