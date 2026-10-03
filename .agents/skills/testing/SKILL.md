---
name: testing
description: "Select and implement the appropriate unit, API, database, or Aspire integration tests for XtreamForge. Use whenever behavior is added, changed, fixed, or refactored."
---

# Purpose

Use this skill whenever behavior is added, changed, fixed, or refactored.

Tests live in:

`tests/XtreamForge.Tests`

The repository uses xUnit.

Aspire.Hosting.Testing is available for distributed application testing.

# Test project setup

`XtreamForge.Tests.csproj` references:

- `XtreamForge.AppHost` (Aspire integration tests)
- `XtreamForge.ApiService` (endpoint/service unit tests)
- `XtreamForge.Domain` (domain logic)
- `Microsoft.EntityFrameworkCore.Sqlite` (in-memory SQLite for non-relational unit tests)
- `Aspire.Hosting.Testing`
- `xUnit`

Test folders mirror the `XtreamForge.ApiService` structure, and namespaces match folders (`XtreamForge.Tests.<Folder>`):

- `Endpoints/Admin/` — admin endpoint classes: HTTP result mapping (`Ok`, `Created` + location, `NotFound`, `Conflict`, `NoContent`). One file per admin resource (for example `ItemRuleEndpointsTests.cs` covers the Get/Post/Put/Delete endpoints of item rules).
- `Services/` — core services (`CategoryRuleService`, `ItemRuleService`, `ItemService`, `CategoryService`).
- `Services/Admin/` — admin services (`*AdminServiceTests.cs`).
- `Xtream/` — Xtream request pipeline (`XtreamProviderValidator`, `XtreamContextBuilder`, `XtreamContext`).
- `Infrastructure/` — shared test helpers only (`SqliteDbContextFactory`).

Tests needing a DbContext use SQLite in-memory + `EnsureCreated()` through `Infrastructure/SqliteDbContextFactory`.

Do NOT add a `Version` to any `<PackageReference>` in the test project; `Directory.Packages.props` manages versions centrally.

# Choose the correct test level

Use the smallest test level that realistically verifies the behavior.

Prefer a unit test for:

- pure domain behavior;
- rule evaluation;
- transformations;
- isolated service logic;
- deterministic calculations.

Prefer API/integration testing when behavior depends on:

- ASP.NET Core routing;
- request binding;
- middleware;
- serialization;
- actual HTTP responses.

Prefer relational/database testing when behavior depends on:

- EF Core translation;
- PostgreSQL/relational semantics;
- database constraints;
- persistence interactions that mocks cannot verify meaningfully.

Prefer Aspire integration testing when behavior depends on:

- application resources;
- service discovery;
- resource startup;
- communication between application projects.

Do not use an expensive integration test when a unit test proves the behavior equally well.

# Test behavior, not implementation

Test observable behavior.

Do not test private methods directly.

Do not use reflection to reach private members.

Do not verify internal call sequences unless call ordering is itself part of required behavior.

A safe internal refactor should not break unrelated tests.

# Bug fixes

When fixing a bug:

1. understand the failure;
2. create a regression test that reproduces it when practical;
3. implement the fix;
4. verify the regression test passes;
5. run related tests.

# Xtream proxy

For proxy behavior, consider testing:

- HTTP method preservation;
- query preservation;
- status code preservation;
- request/response body handling;
- relevant headers;
- transformed payload behavior;
- invalid provider destinations;
- credential redaction;
- transparent forwarding.

Test only the aspects relevant to the change.

# Database testing

Do not use EF Core InMemory to prove relational behavior.

If the behavior is independent of relational semantics, an isolated test can be appropriate.

If SQL translation, constraints, or PostgreSQL behavior matters, choose an appropriate relational/integration test.

# Aspire testing

Use Aspire.Hosting.Testing when the distributed application itself is part of what must be verified.

Do not start the entire distributed application merely to test a pure service.

When using Aspire integration tests:

- wait for required resources;
- use discovered endpoints;
- avoid assuming fixed ports;
- make tests deterministic;
- clean up resources correctly.

# Adding a new test to XtreamForge.Tests

1. Add the `*.cs` file in the folder mirroring the code under test (see "Test project setup"); never at the project root. Use the matching namespace, for example `XtreamForge.Tests.Services.Admin`.
2. Use `public class MyTests` (not sealed — xUnit reflects on it).
3. For tests requiring a DbContext, use the shared factory:
   ```csharp
   _dbContext = SqliteDbContextFactory.Create();
   ```
   Call `_dbContext.ChangeTracker.Clear()` after seeding when the code under test must load fresh state (for example filtered `Include`).
4. Implement `IAsyncDisposable` with `await _dbContext.DisposeAsync()` when holding a DbContext.
5. Do not use `XtreamForge.Database` directly unless the test needs persistence — pure service logic can be tested without a DbContext.
6. For code depending on `HttpContext` (for example `XtreamContext`), use `DefaultHttpContext` with `Request.QueryString`; do not mock ASP.NET Core types.
7. Do not rely on DNS in unit tests: use IP literals or `XtreamProxyOptions.AllowedHosts` when testing `XtreamProviderValidator`.

# Test quality

Tests must not depend on:

- execution order;
- developer-machine state;
- arbitrary delays;
- current wall-clock timing unless controlled;
- external production services.

Prefer precise assertions that communicate the required behavior.

# Analyzer quality

Test code must satisfy the same repository quality requirements as production code.

Respect `.editorconfig`.

Do not introduce compiler or SonarAnalyzer.CSharp diagnostics.

Do not suppress analyzer rules merely because code is in a test project.

# Validation

Run new/modified tests first.

Then run closely related tests.

For broad changes, run:

```
dotnet test XtreamForge.slnx
```

Never state that tests pass unless they were actually executed.
