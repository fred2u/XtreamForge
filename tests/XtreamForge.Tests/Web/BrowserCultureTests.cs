using System.Globalization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using XtreamForge.Web.Configuration;

namespace XtreamForge.Tests.Web;

public class BrowserCultureTests
{
    [Theory]
    [InlineData("fr-BE,fr;q=0.9,en;q=0.8", "fr-BE")]
    [InlineData("en-GB", "en-GB")]
    [InlineData("de", "de")]
    public async Task Request_UsesTheCultureOfTheBrowser(string acceptLanguage, string expected)
    {
        var culture = await CultureOfRequestAsync(acceptLanguage);

        Assert.Equal(expected, culture.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("xx-unknown")]
    public async Task Request_WithoutKnownLanguage_UsesEnglish(string? acceptLanguage)
    {
        var culture = await CultureOfRequestAsync(acceptLanguage);

        Assert.Equal("en-US", culture.Name);
    }

    [Fact]
    public async Task Request_FormatsDatesWithTheCultureOfTheBrowser()
    {
        var culture = await CultureOfRequestAsync("fr-BE");

        Assert.Equal("16/10/2026", new DateTime(2026, 10, 16).ToString("d", culture));
    }

    private static async Task<CultureInfo> CultureOfRequestAsync(string? acceptLanguage)
    {
        var options = new RequestLocalizationOptions();
        BrowserCultureExtensions.Configure(options);

        CultureInfo? culture = null;
        var middleware = new RequestLocalizationMiddleware(
            _ =>
            {
                culture = CultureInfo.CurrentCulture;
                return Task.CompletedTask;
            },
            Options.Create(options),
            NullLoggerFactory.Instance);

        var context = new DefaultHttpContext();
        if (acceptLanguage is not null)
        {
            context.Request.Headers.AcceptLanguage = acceptLanguage;
        }

        await middleware.Invoke(context);
        return culture!;
    }
}
