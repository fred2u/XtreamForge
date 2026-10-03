using Microsoft.Extensions.Options;
using XtreamForge.Web.Features.Categories;
using XtreamForge.Web.Features.Dashboard;
using XtreamForge.Web.Features.History;
using XtreamForge.Web.Features.Monitoring;
using XtreamForge.Web.Features.Recommendations;
using XtreamForge.Web.Features.Sources;
using XtreamForge.Web.Features.Tmdb;

namespace XtreamForge.Web.Configuration;

public static class BackendApiServiceCollectionExtensions
{
    public static IServiceCollection AddBackendApiClients(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<BackendOptions>()
            .BindConfiguration(BackendOptions.SectionName);

        services.AddHttpClient<DashboardClient>(ConfigureBackendClient);
        services.AddHttpClient<MonitoringClient>(ConfigureBackendClient);
        services.AddHttpClient<CategoriesClient>(ConfigureBackendClient);
        services.AddHttpClient<SourcesClient>(ConfigureBackendClient);
        services.AddHttpClient<TmdbInfosClient>(ConfigureBackendClient);
        services.AddHttpClient<TmdbMappingsClient>(ConfigureBackendClient);
        services.AddHttpClient<WatchHistoryClient>(ConfigureBackendClient);
        services.AddHttpClient<RecommendationsClient>(ConfigureBackendClient);

        return services;
    }

    private static void ConfigureBackendClient(IServiceProvider serviceProvider, HttpClient client)
    {
        var options = serviceProvider.GetRequiredService<IOptions<BackendOptions>>().Value;
        client.BaseAddress = ResolveBaseUri(options);
    }

    internal static Uri ResolveBaseUri(BackendOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out var baseUri))
        {
            throw new InvalidOperationException("Backend:BaseUrl must be an absolute URI.");
        }

        return baseUri;
    }
}
