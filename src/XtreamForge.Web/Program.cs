using Microsoft.FluentUI.AspNetCore.Components;
using XtreamForge.ServiceDefaults;
using XtreamForge.Web.Components;
using XtreamForge.Web.Configuration;
using XtreamForge.Web.Features.Categories;
using XtreamForge.Web.Features.Tmdb;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseStaticWebAssets();

builder.AddServiceDefaults();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddHttpClient();
builder.Services.AddFluentUIComponents();
builder.Services.AddBackendApiClients();
builder.Services.AddScoped<CategoryScreenState>();
builder.Services.AddScoped<TmdbScreenState>();

var app = builder.Build();

app.MapDefaultEndpoints();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

await app.RunAsync();
