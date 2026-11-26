var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postserv")
    .WithDataVolume("xtreamforge-postserv-data")
    .WithPgAdmin();

var database = postgres.AddDatabase("database", "xtreamforge");

// the Xtream proxy must be reachable by the IPTV devices; the admin API it also serves is not authenticated yet
var apiService = builder.AddDotnetProject("xtreamforge-apiservice", "../XtreamForge.ApiService/XtreamForge.ApiService.csproj")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(database)
    .WaitFor(database);

builder.AddDotnetProject("xtreamforge-blazor", "../XtreamForge.Web/XtreamForge.Web.csproj")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

await builder.Build().RunAsync();
