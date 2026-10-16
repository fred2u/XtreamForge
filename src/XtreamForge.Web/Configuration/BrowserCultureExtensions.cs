using System.Globalization;
using Microsoft.AspNetCore.Localization;

namespace XtreamForge.Web.Configuration;

/// <summary>
/// Formats dates, times and numbers with the culture of the browser (its Accept-Language header) instead of the culture
/// of the server, a container usually running with the invariant culture. The interface text itself stays in English.
/// </summary>
public static class BrowserCultureExtensions
{
    // used when the browser sends no language, or only languages unknown to the server
    private const string DefaultCulture = "en-US";

    public static IServiceCollection AddBrowserCulture(this IServiceCollection services) =>
        services.Configure<RequestLocalizationOptions>(Configure);

    /// <summary>
    /// Every culture known to the server is accepted. The interactive circuit keeps the culture of the request that started it.
    /// </summary>
    public static void Configure(RequestLocalizationOptions options)
    {
        var cultures = CultureInfo.GetCultures(CultureTypes.AllCultures)
            .Where(culture => !string.IsNullOrEmpty(culture.Name))
            .ToList();

        options.DefaultRequestCulture = new RequestCulture(DefaultCulture);
        options.SupportedCultures = cultures;
        options.SupportedUICultures = cultures;
        options.RequestCultureProviders = [new AcceptLanguageHeaderRequestCultureProvider()];
    }
}
