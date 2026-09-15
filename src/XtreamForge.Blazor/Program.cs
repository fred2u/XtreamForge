using XtreamForge.Blazor.Components;
using XtreamForge.Blazor.Features.Categories;
using XtreamForge.Blazor.Features.Dashboard;

namespace XtreamForge.Blazor;

public sealed class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        builder.AddServiceDefaults();

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        var backendBaseUrl = builder.Configuration["Backend:BaseUrl"] ?? "http://xtreamforge";
        builder.Services.AddHttpClient<DashboardClient>(client => client.BaseAddress = new Uri(backendBaseUrl));
        builder.Services.AddHttpClient<CategoriesClient>(client => client.BaseAddress = new Uri(backendBaseUrl));

        var app = builder.Build();

        app.MapDefaultEndpoints();
        app.UseAntiforgery();
        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }
}
