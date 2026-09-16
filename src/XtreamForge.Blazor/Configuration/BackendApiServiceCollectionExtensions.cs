using Microsoft.Extensions.Options;
using XtreamForge.Blazor.Features.Categories;
using XtreamForge.Blazor.Features.Dashboard;
using XtreamForge.Blazor.Features.Items.ItemRules;

namespace XtreamForge.Blazor.Configuration;

public static class BackendApiServiceCollectionExtensions
{
    public static IServiceCollection AddBackendApiClients(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions<BackendOptions>()
            .BindConfiguration(BackendOptions.SectionName);

        services.AddHttpClient<DashboardClient>(ConfigureBackendClient);
        services.AddHttpClient<CategoriesClient>(ConfigureBackendClient);
        services.AddHttpClient<ItemRulesClient>(ConfigureBackendClient);

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
