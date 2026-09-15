using XtreamForge.Blazor.Components;
using XtreamForge.Blazor.Configuration;
using Microsoft.AspNetCore.Hosting;

namespace XtreamForge.Blazor;

public sealed class Program
{
    public static void Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);
        builder.WebHost.UseStaticWebAssets();

        builder.AddServiceDefaults();

        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        builder.Services.AddBackendApiClients();

        var app = builder.Build();

        app.MapDefaultEndpoints();
        app.UseAntiforgery();
        app.MapStaticAssets();
        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        app.Run();
    }
}
