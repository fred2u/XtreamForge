var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("xtreamforge-postgres-data");

var database = postgres.AddDatabase("database", "xtreamforge");

var api = builder.AddProject<Projects.XtreamForge_Api>("api")
    .WithReference(database)
    .WaitFor(database);

builder.AddProject<Projects.XtreamForge_Admin>("admin")
    .WithReference(database)
    .WithReference(api)
    .WaitFor(database)
    .WaitFor(api);

builder.Build().Run();
