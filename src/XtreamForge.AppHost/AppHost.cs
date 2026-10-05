var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postserv")
    .WithDataVolume("xtreamforge-postserv-data")
    .WithPgAdmin();

var database = postgres.AddDatabase("database", "xtreamforge");

// the Xtream proxy must be reachable by the IPTV devices; the admin API it also serves is not authenticated yet
var apiService = builder.AddProject<Projects.XtreamForge_ApiService>("xtreamforge-apiservice")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(database)
    .WaitFor(database);

builder.AddProject<Projects.XtreamForge_Web>("xtreamforge-blazor")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

await builder.Build().RunAsync();
