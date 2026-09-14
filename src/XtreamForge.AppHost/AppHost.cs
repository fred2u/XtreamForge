var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume("xtreamforge-postgres-data");

var database = postgres.AddDatabase("database", "xtreamforge");

builder.AddProject<Projects.XtreamForge>("xtreamforge")
    .WithReference(database)
    .WaitFor(database);

builder.Build().Run();
