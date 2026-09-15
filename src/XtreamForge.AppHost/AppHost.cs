var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("xtreamforge-postgres-data")
    .WithPgAdmin();

var database = postgres.AddDatabase("database", "xtreamforge");

var backend = builder.AddProject<Projects.XtreamForge>("xtreamforge")
    .WithReference(database)
    .WaitFor(database);

builder.AddProject<Projects.XtreamForge_Blazor>("xtreamforge-blazor")
    .WithReference(backend)
    .WaitFor(backend);

builder.Build().Run();
