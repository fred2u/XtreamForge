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

            services.AddOptions<XtreamProxyOptions>()
                .BindConfiguration(XtreamProxyOptions.SectionName);

            return services;
        }
    }
}
