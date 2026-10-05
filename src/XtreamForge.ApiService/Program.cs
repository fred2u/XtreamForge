using Microsoft.AspNetCore.ResponseCompression;
using XtreamForge.ApiService.Endpoints.Admin;
using XtreamForge.ApiService.Endpoints.Xtream;
using XtreamForge.ApiService.Infrastructure;
using XtreamForge.ServiceDefaults;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();

// the request logs and the request log scope (RequestPath, attached to every log of the request) write the raw path,
// which carries the stream credentials; the redacted server spans describe the requests instead
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.None);

builder.Services.AddBindedOptions();
builder.Services.AddForwardedHeaders();
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

// first, so that the scheme, host, and client address of the original request are seen by every middleware and endpoint
app.UseForwardedHeaders();
app.UseResponseCompression();

app.UseDatabase();

app.MapDefaultEndpoints();
app.MapXtreamEndpoints();
app.MapAdminEndpoints();

await app.RunAsync();
