using Microsoft.Extensions.Options;
using XtreamForge.ApiService.Options;

namespace XtreamForge.ApiService.Infrastructure;

public static class BindOptionsExtensions
{
    extension(IServiceCollection services)
    {
        public IServiceCollection AddBindedOptions()
        {
            services.AddOptions<TmdbOptions>()
                .BindConfiguration(TmdbOptions.SectionName)
                .Validate(options => Uri.TryCreate(options.BaseUrl, UriKind.Absolute, out _), $"{TmdbOptions.SectionName}:BaseUrl must be an absolute URI.")
                .Validate(
                    options => Uri.TryCreate(options.ImageBaseUrl, UriKind.Absolute, out var imageBaseUri) && (imageBaseUri.Scheme == Uri.UriSchemeHttps || imageBaseUri.Scheme == Uri.UriSchemeHttp),
                    $"{TmdbOptions.SectionName}:ImageBaseUrl must be an absolute HTTP(S) URI.")
                .ValidateOnStart();

            // without any allowed destination, every upstream request would be rejected
            services.AddOptions<XtreamProxyOptions>()
                .BindConfiguration(XtreamProxyOptions.SectionName)
                .Validate(
                    options => options.AllowAnyDestination || options.AllowedHosts.Any(host => !string.IsNullOrWhiteSpace(host)),
                    $"{XtreamProxyOptions.SectionName}:AllowedHosts must list at least one upstream host when {XtreamProxyOptions.SectionName}:AllowAnyDestination is false.")
                .ValidateOnStart();

            services.AddOptions<RecommendationOptions>()
                .BindConfiguration(RecommendationOptions.SectionName)
                .Validate(options => options.CategoryId > 0, $"{RecommendationOptions.SectionName}:CategoryId must be a positive number.")
                .ValidateOnStart();

            services.AddOptions<PopularOptions>()
                .BindConfiguration(PopularOptions.SectionName)
                .Validate(options => options.CategoryId > 0, $"{PopularOptions.SectionName}:CategoryId must be a positive number.")
                .Validate<IOptions<RecommendationOptions>>(
                    (options, recommendationOptions) => options.CategoryId != recommendationOptions.Value.CategoryId,
                    $"{PopularOptions.SectionName}:CategoryId must differ from {RecommendationOptions.SectionName}:CategoryId.")
                .ValidateOnStart();

            return services;
        }
    }
}
