using Microsoft.AspNetCore.ResponseCompression;
using XtreamForge.ApiService.Endpoints.Admin;
using XtreamForge.ApiService.Endpoints.Xtream;
using XtreamForge.ApiService.Infrastructure;
using XtreamForge.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

builder.Services.AddBindedOptions();
builder.Services.AddDatabase();
builder.Services.AddHttpClients();
builder.Services.AddServices();
builder.Services.AddCustomHealthChecks();

builder.Services.AddResponseCompression(options =>
{
    options.EnableForHttps = true;
    options.MimeTypes = ResponseCompressionDefaults.MimeTypes;
});

var app = builder.Build();

app.UseResponseCompression();

app.UseDatabase();

app.MapDefaultEndpoints();
app.MapXtreamEndpoints();
app.MapAdminEndpoints();

await app.RunAsync();
