var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postserv")
    .WithDataVolume("xtreamforge-postserv-data")
    .WithPgAdmin();

var database = postgres.AddDatabase("database", "xtreamforge");

var apiService = builder.AddProject<Projects.XtreamForge_ApiService>("xtreamforge-apiservice")
    .WithHttpHealthCheck("/health")
    .WithReference(database)
    .WaitFor(database);

builder.AddProject<Projects.XtreamForge_Web>("xtreamforge-blazor")
    .WithExternalHttpEndpoints()
    .WithHttpHealthCheck("/health")
    .WithReference(apiService)
    .WaitFor(apiService);

await builder.Build().RunAsync();
