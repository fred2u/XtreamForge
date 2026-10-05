---
name: aspire
description: "Configure XtreamForge distributed application resources, service discovery, PostgreSQL, health checks, and dependencies using .NET Aspire. Use when changing XtreamForge.AppHost, Aspire resources, service relationships, PostgreSQL orchestration, service discovery, health checks, or shared service defaults."
---

# Purpose

Use this skill when changing:

- `XtreamForge.AppHost`;
- Aspire resources;
- service relationships;
- PostgreSQL orchestration;
- service discovery;
- health checks;
- shared service defaults.

# Architecture

`XtreamForge.AppHost` owns development-time orchestration.

The current conceptual dependency flow is:

```
PostgreSQL
    ↓
ApiService (XtreamForge.ApiService)
    ↓
Web (XtreamForge.Web)
```

Current `AppHost.cs` wiring (read before modifying):

```csharp
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
```

Named resources in use: `postserv`, `database`, `xtreamforge-apiservice`, `xtreamforge-blazor`.
Do not rename these without understanding downstream impact on service discovery and connection strings.

Preserve explicit resource references and startup dependencies where appropriate.

# Resource wiring

Prefer Aspire resource references over manually constructed development connection strings or service URLs.

ApiService obtains its database connection from the Aspire PostgreSQL/database resource.

Web reaches ApiService through service discovery.

Do not hard-code Aspire-generated ports.

Do not assume a localhost port remains constant.

# PostgreSQL

When changing the PostgreSQL resource:

- preserve persistent storage when appropriate;
- pass the database resource to ApiService using Aspire references;
- never hard-code production credentials;
- do not expose database access to Web.

# ServiceDefaults

Use XtreamForge.ServiceDefaults only for genuinely shared hosting concerns such as:

- OpenTelemetry;
- health checks;
- service discovery;
- HTTP resilience;
- shared instrumentation.

Application-specific behavior does not belong in ServiceDefaults.

# Adding a resource

Before introducing another Aspire resource:

1. establish why the application needs it;
2. determine which project consumes it;
3. add the resource in AppHost;
4. add only required references;
5. define appropriate startup dependencies;
6. configure health/readiness behavior when useful.

Do not add infrastructure speculatively.

# Validation

After Aspire changes:

- build AppHost;
- build dependent projects;
- run relevant Aspire integration tests;
- when practical, start AppHost and verify resource health.

Do not replace service discovery with fixed development URLs just to solve a discovery problem.
