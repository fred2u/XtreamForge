# XtreamForge Repository Instructions

These instructions apply to all work in the XtreamForge repository.

The current source code and project files are the source of truth.

The repository is undergoing a deliberate simplification/rewrite. Prefer the current architecture and current implementation over assumptions derived from older code, documentation, previous architecture, or historical patterns.

If README documentation conflicts with the current source tree or project files, follow the current implementation and mention that the documentation should be updated.

# Primary Engineering Goal

XtreamForge should remain simple to understand, maintain, debug, and extend.

Prefer:

- KISS over ceremony;
- straightforward code over clever abstractions;
- explicit behavior over hidden conventions;
- small cohesive classes and methods;
- pragmatic SOLID;
- standard .NET capabilities;
- minimal dependencies;
- code that can be understood locally without navigating through many abstraction layers.

Do not recreate complexity that has intentionally been removed during the rewrite.

A solution is not better merely because it introduces more layers, interfaces, patterns, or abstractions.

# Technology

The solution uses:

- .NET 10;
- modern C# supported by the configured .NET 10 SDK;
- ASP.NET Core;
- Minimal APIs;
- Blazor Web App with Interactive Server rendering;
- Entity Framework Core;
- PostgreSQL;
- Npgsql;
- .NET Aspire;
- OpenTelemetry;
- Microsoft Fluent UI Blazor components;
- xUnit v3 on Microsoft Testing Platform;
- SQLite in-memory (tests only);
- SonarAnalyzer.CSharp.

The SDK version is defined by `global.json`.

Do not downgrade the target framework, SDK, packages, or tooling unless explicitly requested.

# Current Solution Structure

The current solution contains:

`src/XtreamForge.ApiService`
- ASP.NET Core backend;
- Minimal API endpoints;
- application services;
- Xtream request processing;
- upstream HTTP communication;
- TMDB API communication and matching (`Services/Tmdb`);
- hosted background services;
- API-specific infrastructure;
- application configuration.

`src/XtreamForge.Domain`
- domain entities;
- domain enums;
- domain concepts shared independently of infrastructure.

`src/XtreamForge.Database`
- EF Core DbContext;
- entity mappings;
- PostgreSQL persistence;
- migrations;
- persistence-specific configuration.

`src/XtreamForge.Web`
- Blazor administration application;
- UI components;
- feature-oriented UI code;
- HTTP clients communicating with ApiService.

`src/XtreamForge.ServiceDefaults`
- common Aspire service defaults;
- OpenTelemetry;
- health checks;
- service discovery;
- resilience;
- cross-service observability concerns.

`src/XtreamForge.AppHost`
- Aspire orchestration;
- PostgreSQL resource;
- ApiService resource;
- Web resource;
- development-time resource dependencies.

`tests/XtreamForge.Tests`
- automated unit and endpoint tests using xUnit v3;
- SQLite in-memory databases for database-backed tests (`Infrastructure/SqliteDbContextFactory`);
- stub `IHttpClientFactory` implementations for upstream Xtream and TMDB calls (`Infrastructure/Stub*HttpClientFactory`).

Respect these boundaries.

Do not move responsibilities between projects without a concrete reason.

# Dependency Direction

Keep dependencies simple.

The intended direction is broadly:

ApiService -> Database -> Domain

Web -> ApiService over HTTP

AppHost -> application projects for orchestration

ServiceDefaults -> shared hosting/observability infrastructure

Domain must remain independent of:

- EF Core;
- PostgreSQL/Npgsql;
- ASP.NET Core;
- Blazor;
- Aspire;
- HTTP infrastructure.

Database may depend on Domain.

ApiService may depend on Database and ServiceDefaults.

Web must not access Database or DbContext directly.

Do not add project references that violate these boundaries unless explicitly justified.

# Avoid Architecture Inflation

Do not introduce architectural patterns merely because they are common in enterprise .NET applications.

In particular, do not introduce by default:

- generic Repository patterns;
- Unit of Work wrappers around EF Core;
- MediatR;
- CQRS infrastructure;
- AutoMapper;
- command/query handler layers for trivial operations;
- interface/implementation pairs with only one implementation and no meaningful boundary;
- unnecessary factories;
- unnecessary managers;
- unnecessary coordinators;
- unnecessary wrapper services;
- abstraction layers that only forward calls unchanged.

`DbContext` is already an abstraction over persistence.

`HttpClient` and its configured clients are already abstractions over HTTP communication.

Add abstractions when they solve a real problem such as:

- multiple implementations;
- an external-system boundary;
- meaningful test isolation;
- substantial reusable behavior;
- separation of genuinely different responsibilities.

Prefer deleting unnecessary indirection over adding more indirection.

# ApiService

`XtreamForge.ApiService` owns backend application behavior.

Keep `Program.cs` focused on application composition:

- service registration;
- middleware;
- endpoint registration;
- application startup.

Prefer extension methods for coherent groups of registrations when this keeps `Program.cs` readable.

Do not hide simple setup behind excessive layers of extension methods.

# Minimal API Endpoints

Endpoint classes should primarily handle the HTTP boundary.

Keep endpoint responsibilities focused on:

- route definition;
- request binding;
- input validation;
- invoking the appropriate service/operation;
- translating results to HTTP responses.

Business logic that is significant or reusable belongs outside endpoint definitions.

Do not introduce controllers unless there is a concrete benefit over the existing Minimal API approach.

Use endpoint DTOs when the HTTP contract differs from domain or persistence models.

Do not expose EF Core entities automatically as public API contracts when doing so creates unwanted coupling.

# Services

Application services should represent meaningful application operations or cohesive behavior.

Keep services focused.

Avoid creating a service whose only purpose is forwarding every method call to another service.

Before creating a new service:

1. check whether the behavior naturally belongs to an existing cohesive service;
2. check whether it belongs to the domain;
3. check whether it is infrastructure-specific;
4. create a new service only when it provides a meaningful responsibility boundary.

# Xtream Request Pipeline

Xtream-related HTTP processing currently lives under:

`XtreamForge.ApiService/Xtream`

and endpoint-specific behavior under:

`XtreamForge.ApiService/Endpoints/Xtream`.

Preserve this separation when it remains useful.

The Xtream context represents request-specific information used while processing an incoming Xtream request.

Use dedicated components such as context building, request-message creation, provider validation, and response writing only where they maintain a clear single responsibility.

Do not merge components merely to reduce file count if that makes responsibilities unclear.

Conversely, do not split small cohesive behavior across additional classes without a clear benefit.

# TMDB Enrichment

Items returned by `get_vod_streams` / `get_series` need a known TMDB ID. `get_vod_info` / `get_series_info` (`ItemGetEndpoint`) apply the same category, item rule, and TMDB conditions through `ItemService.TransformInfo`, return the empty Xtream payload otherwise, and never enqueue a TMDB ID lookup.

Missing TMDB IDs are resolved asynchronously by `TmdbIdRetrieverBackgroundService`:

- requests are queued through the singleton `TmdbIdRetrieverQueue` (injected into `ItemService` and the background service) and deduplicated per `Source + ContentType + StreamId`;
- each request is processed in its own DI scope by `TmdbIdRetrieverService`;
- the provider `get_vod_info` / `get_series_info` payload is used first, the TMDB search (`TmdbIdMatcher`) is only a fallback;
- only confident matches are persisted: avoiding false positives takes precedence over finding more matches;
- a lookup without result or failing is persisted in `StreamTmdbMapping` with a null `TmdbId`, `LookupAttemptCount`, and `NextLookupAtUtc` (delay of 1 day doubling up to 30 days); the source snapshot exposes these not-yet-due streams as `DeferredTmdbLookups` so `ItemService` does not enqueue them. Code reading `StreamTmdbMappings` must treat a null `TmdbId` as "not mapped".
- mappings can be corrected or set manually by the admin (`StreamTmdbMappingAdminService`); a mapped stream is never looked up again, so a persisted mapping must win over the provider `tmdb_id` of the `get_vod_info` / `get_series_info` payload (`ItemService.TransformInfo`).

TMDB metadata (`Domain/Tmdb/TmdbInfo`, one entry per `ContentType + TmdbId`) enriches the returned items:

- it is only loaded from the TMDB details, in the background by `TmdbInfoBackgroundService` through the singleton `TmdbInfoQueue` (refresh after 60 days, retry delay of 1 day doubling up to 30 days; a request for an entry that is not due is ignored); `TmdbIdRetrieverService` enqueues every TMDB ID it finds, whatever its source; TMDB search results are never stored; all writes go through `TmdbInfoService`;
- list items are enriched by batches (`ItemsGetEndpoint` reads the metadata of each batch with one query): do not preload the metadata of a whole catalogue;
- `TmdbItemEnricher` merges both sources: it only replaces keys already present in the provider item and only with available TMDB values (TMDB wins when both have a value, except `genre`, filled from the English TMDB genres only when the provider value is empty); TMDB and provider values are both untrusted;
- an item that cannot be enriched (no positive TMDB ID, metadata not loaded) is excluded from the response and appears once its metadata is loaded;
- item rules are applied once, on the provider name; after the enrichment, the TMDB rules (`TmdbRule`, global per content type, on the TMDB title or genres, evaluated by `TmdbRuleService`) and the manual exclusion of the TMDB metadata (`TmdbInfo.IsExcluded`) decide. Manually excluded entries are only loaded as IDs (`TmdbInfoLookup.ExcludedTmdbIds`) and never enqueued for loading.

TMDB matching lives under `XtreamForge.ApiService/Services/Tmdb`:

- `TmdbClient` is the only component calling the TMDB API, through the named `HttpClient` configured from `TmdbOptions`;
- candidates are scored by `ITmdbScoringRule` implementations (one class per rule, `Basic` or `Advanced` stage, `AppliesTo` per content type);
- add a scoring rule by creating a new `ITmdbScoringRule` implementation and registering it in `DependenciesExtensions`, not by adding conditions to the matcher.

Upstream credentials needed by background work must stay in memory only and must never be exposed through `ToString`, logs, or exceptions.

# Watch History

Movie playbacks are recorded in `WatchHistory` (`Domain/History/WatchHistoryEntry`: content type, TMDB ID, start date; one entry per playback, no account nor source):

- `XtreamRequestForwardEndpoint` reports each `GET` of a `movie/{username}/{password}/{streamId}.{ext}` path served (2xx) or redirected (3xx, redirects are not followed) by the provider (`XtreamStreamPath.ParseMovie`) to the singleton `WatchHistoryQueue` for as long as the stream is written; the stream route must keep not resolving a `DbContext`;
- the queue groups the requests of one playback in memory (same source, account, and stream; running request or last one ended less than `PlaybackIdleTimeout` ago) and enqueues only new playbacks;
- `WatchHistoryBackgroundService` resolves the TMDB ID through `WatchHistoryService`: the persisted `StreamTmdbMapping` first, then the provider `get_vod_info` payload; the credentials of the stream URL stay in memory only;
- series episodes are not recorded: their stream URL only carries the episode ID, unknown to the `StreamTmdbMapping` keyed by series ID.
- the admin can delete a playback and add one, started now, from a `TmdbInfo` entry (`WatchHistoryAdminService`).

Recommendations (`Services/RecommendationService`, admin page `/recommendations`) are computed from the TMDB `movie/{id}/recommendations` of the most recently watched movies; a movie of the watch history or manually excluded must never be recommended. Nothing is persisted: the Xtream requests read the recommended TMDB IDs through the singleton `TmdbIdCache` (6 hours, key `RecommendationsKey`), which every watch history change must invalidate.

Virtual categories (`Services/VirtualCategoryService`) are filled from TMDB IDs instead of provider categories: recommendations (VOD only, `RecommendationOptions`) then popular (VOD and series, `PopularOptions`, `PopularService` reading 5 pages of TMDB `movie/popular` / `tv/popular`), in this priority order. They are listed first by `CategoriesGetEndpoint`; for `category_id=ALL` (`ItemsGetEndpoint`) an item is moved to the first virtual category containing it; a requested virtual category calls the provider with `ALL` and lists only its items. A TMDB ID is assigned once per response (`VirtualCategoryAssignment`): the first stream in provider order takes the virtual category, the next ones keep their provider category. `get_*_info` (`ItemGetEndpoint`) never applies the virtual categories, as a single item cannot know whether it is that first stream. Add a virtual category in `VirtualCategoryService`, not in the endpoints.

The authentication (`player_api.php` without action, `RequestAction.Authenticate`) is handled by `AuthenticateEndpoint`: when `user_info.auth` is 1, `server_info` is rewritten to the host, port, and scheme of the incoming request, and the account upstream is remembered in the singleton `XtreamAccountDirectory` (memory only, keyed by credentials). The `/movie|series|live/{username}/{password}/{file}` routes (no upstream prefix) resolve the upstream from it and reuse `XtreamContextBuilder` (destination validation) and `XtreamRequestForwardEndpoint`; like the prefixed stream route, they must not resolve a `DbContext`.

# Xtream Proxy Correctness

XtreamForge sits between an Xtream-compatible client and upstream providers.

When proxying requests, preserve relevant upstream semantics unless XtreamForge intentionally transforms them.

Consider:

- HTTP method (the proxy routes only accept `GET` and `HEAD`, so no request body is forwarded);
- URI;
- query parameters;
- relevant headers;
- status code;
- response headers;
- streaming;
- cancellation;
- provider-specific payload fields.

Background queues are monitored by `Services/Monitoring/QueueMonitor`: a new queue implements `IMonitoredQueue` (stable `Name`, waiting `Count`), is registered both as itself and as `IMonitoredQueue`, and its consumer reports each item with `QueueMonitor.RecordProcessed` (`Succeeded`, `NoResult`, `Failed`). The Monitoring page of the admin UI and the OpenTelemetry metrics then include it without further change.

HTTP 429 from TMDB or an Xtream provider is handled by `Infrastructure/RateLimiting`: `RateLimitHandler` is inserted as the outermost handler of both named clients (before the resilience handler, so its waits are not counted by the resilience timeouts) and paces each upstream host through the singleton `UpstreamRateLimiter` (`Retry-After`, doubling interval, gradual recovery, up to three GET/HEAD attempts). The standard resilience options are configured so that 429 is neither retried nor counted by the circuit breaker. Do not add 429 retry loops in callers.

Upstream failures of the Xtream endpoints go through `Xtream/XtreamUpstreamFailure`: `catch (Exception exception) when (XtreamUpstreamFailure.IsUpstreamFailure(exception, cancellationToken))` then `XtreamUpstreamFailure.Handle` (504 for a timeout, 502 otherwise, logged with the action and upstream host; once the response has started, the connection is aborted instead, as the status can no longer change). Do not repeat per-endpoint `try/catch` blocks. A streamed response must be started explicitly (`Response.StartAsync`) before writing, with its `Content-Type`.

Avoid buffering complete payloads when streaming is possible and no transformation requires buffering.

Avoid unnecessary JSON deserialization when a response can be transparently forwarded.

# Security

Treat all incoming proxy information as untrusted.

The proxy has SSRF and credential-leakage risks.

Preserve validation of:

- protocol;
- host;
- port;
- configured upstream authorization restrictions.

Never weaken destination validation merely to make a scenario work.

Do not log or expose:

- Xtream usernames;
- Xtream passwords;
- API keys;
- connection strings;
- credentials embedded in query strings;
- other secrets.

Sensitive information must not appear in:

- logs;
- exceptions;
- OpenTelemetry spans;
- activity tags;
- metrics;
- health check output;
- diagnostic URLs.

Use existing redaction mechanisms:

- `XtreamCredentialRedaction` (ServiceDefaults) redacts the `username` / `password` query parameters and the credentials of the stream paths (`movie|series|live|timeshift/{username}/{password}/...`); it is applied to the server and client spans, and `SanitizeText` must wrap any logged text that may contain an upstream URL;
- credentials without a recognizable path shape (short live form `{username}/{password}/{streamId}`) are redacted from the `username` / `password` route values (`XtreamCredentialRedaction.UsernameRouteValue` / `PasswordRouteValue`): the server spans are redacted again once routed (`RedactServerResponse`), and `XtreamHttpRequestMessageFactory` marks them on the upstream request (`SetPathCredentials`) for the client spans and `XtreamHttpClientLogger` (`RedactRequestUri`). A new route carrying credentials in its path must name them `username` / `password`;
- `Microsoft.AspNetCore.Hosting.Diagnostics` is disabled in `Program.cs` (`LogLevel.None`): its request logs and its log scope (`RequestPath`, exported with every log) write the raw path. Do not re-enable it;
- the Xtream HTTP client does not use the default `IHttpClientFactory` logging, which writes the request URL: `XtreamHttpClientLogger` logs the redacted URL instead. Do not add loggers that write raw upstream URLs;
- log caught exceptions with the exception object (SonarAnalyzer rule S6667) and `SanitizeText(exception.Message)`; never put a request URL in an exception message.

Never commit credentials.

# Domain

`XtreamForge.Domain` contains domain concepts, not infrastructure concerns.

Current domain areas include:

- Categories;
- Items;
- History;
- Sources;
- Tmdb;
- shared domain enums.

Keep domain types free of persistence and web-framework dependencies.

Do not add EF Core attributes to domain entities when mappings can live cleanly in `XtreamForge.Database/Mappings`.

Prefer explicit domain concepts over primitive flags when an existing enum or domain type represents the behavior clearly.

Do not introduce elaborate DDD infrastructure.

This is not a requirement to create:

- aggregates;
- domain events;
- value objects;
- specifications;
- domain services;

unless they solve an actual problem in XtreamForge.

# Database

`XtreamForge.Database` owns persistence.

Keep:

- `XtreamForgeDbContext`;
- EF Core configuration;
- PostgreSQL-specific behavior;
- migrations;
- design-time EF infrastructure;

inside this project.

Use `IEntityTypeConfiguration<T>` mappings following the existing structure under `Mappings`.

Do not move PostgreSQL-specific configuration into Domain or Web.

# Entity Framework Core

Use EF Core directly and idiomatically.

When querying:

- prefer server-side filtering;
- avoid N+1 queries;
- avoid unnecessary round trips;
- select only required data where meaningful;
- use async APIs for database I/O;
- use no-tracking queries for read-only work when appropriate;
- consider query translation to PostgreSQL;
- avoid materializing large datasets prematurely.

Do not use EF Core InMemory as evidence that PostgreSQL-specific behavior works.

When relational behavior matters, test against an appropriate relational strategy.

# PostgreSQL

PostgreSQL is the application's primary database.

Production behavior must be designed for PostgreSQL semantics.

The Aspire AppHost defines PostgreSQL and provides the `database` resource to ApiService.

Do not introduce hard-coded database hosts, ports, usernames, passwords, or connection strings.

Use the standard .NET connection-string configuration supplied by Aspire or the runtime environment.

# Migrations

Database schema changes must use EF Core migrations.

Do not edit old applied migrations merely to make the current model match.

Create a new migration for a new schema change.

Keep migrations focused.

Do not create migrations when no schema change occurred.

Do not manually modify the model snapshot except when there is a specific justified reason.

# Aspire

`XtreamForge.AppHost` is the source of truth for local orchestration.

Current resource flow is:

PostgreSQL/database
    -> ApiService
    -> Web

ApiService waits for the database.

Web references and waits for ApiService.

Preserve Aspire service discovery instead of introducing fixed localhost dependencies.

When adding infrastructure needed for development, first consider whether it belongs in AppHost.

Use persistent development storage only where appropriate.

# ServiceDefaults

`XtreamForge.ServiceDefaults` owns genuinely shared hosting defaults.

Use it for cross-service concerns such as:

- OpenTelemetry;
- standard health checks;
- service discovery;
- resilience defaults;
- shared credential-redaction instrumentation.

Do not put application business logic in ServiceDefaults.

Do not turn ServiceDefaults into a general-purpose utilities project.

# Web

`XtreamForge.Web` owns the administration UI.

It is a Blazor application.

Keep feature-specific UI code grouped by feature under `Features`.

For example:

`Features/Dashboard`

A feature may contain:

- Razor components/pages;
- feature-specific HTTP clients;
- feature-specific presentation models;
- small UI helpers.

Keep feature-related code together when this improves discoverability.

The Web project must communicate with ApiService over HTTP.

It must not:

- reference the Database project;
- instantiate `XtreamForgeDbContext`;
- query PostgreSQL directly;
- duplicate backend business logic.

# Fluent UI

The Web project uses Microsoft Fluent UI Blazor components.

Prefer the existing component library and current UI conventions before introducing another UI library.

Do not add a second component framework for functionality Fluent UI already provides adequately.

# HTTP and Service Discovery

Use dependency-injected `HttpClient` configuration.

For communication between Web and ApiService under Aspire, use service discovery rather than hard-coded development URLs.

Do not scatter endpoint base URLs throughout application code.

Keep HTTP configuration in the appropriate configuration/infrastructure location.

# Configuration

Use the standard .NET configuration system.

Prefer:

- strongly typed options for coherent configuration;
- startup validation for required configuration where appropriate;
- environment variables;
- user secrets for local secrets;
- Aspire resource wiring.

Do not read environment variables directly throughout business code when the options/configuration system is more appropriate.

Do not introduce static global configuration.

# Dependency Injection

Prefer constructor injection.

Register dependencies explicitly in the appropriate composition root or registration extension.

Do not use service locator patterns.

Do not call `IServiceProvider.GetService` from normal application/business code unless framework integration genuinely requires it.

Choose service lifetimes deliberately.

Be especially careful with:

- DbContext scope;
- HttpClient lifetime;
- hosted/background services;
- objects captured by singletons.

# C# Style

Follow the repository's `.editorconfig`.

The applicable `.editorconfig` is authoritative.

Current baseline includes:

- UTF-8;
- LF line endings;
- final newline;
- no trailing whitespace;
- four-space indentation for C#/Razor;
- two-space indentation for JSON/YAML.

Do not reformat unrelated code.

Use modern C# when it improves readability, but do not rewrite working code merely to use newer syntax.

Prefer readable constructs over clever or compressed expressions.

# Nullable Reference Types

Nullable reference types are enabled globally.

Model nullability accurately.

Do not use the null-forgiving operator `!` simply to silence the compiler.

Use `!` only when an invariant is real, understood, and cannot reasonably be represented to static analysis.

Validate external input at boundaries.

# Compiler and Analyzer Rules

`TreatWarningsAsErrors` is enabled globally.

`AnalysisLevel` is set to `latest`.

`SonarAnalyzer.CSharp` is applied to C# projects.

New or modified code must compile without warnings or analyzer errors.

Do not work around diagnostics by:

- `#pragma warning disable`;
- `SuppressMessage`;
- disabling Sonar rules;
- lowering diagnostic severity;
- changing `.editorconfig`;
- changing `Directory.Build.props`;
- excluding files from analysis.

Fix the underlying issue.

If a diagnostic is genuinely incorrect or unavoidable, explain it before introducing a suppression.

Do not broaden the scope of a task merely to repair unrelated pre-existing diagnostics.

# Async

Use async all the way for I/O.

Prefer cancellation-aware APIs where appropriate.

Propagate `CancellationToken` through:

- HTTP requests;
- EF Core queries;
- streamed responses;
- other potentially long-running I/O.

Avoid:

- `.Result`;
- `.Wait()`;
- `.GetAwaiter().GetResult()`;
- unnecessary `Task.Run`.

Do not create `async` methods that contain no asynchronous work merely for consistency.

# Error Handling

Do not use exceptions for normal control flow.

Catch an exception only when the code can meaningfully:

- recover;
- translate it;
- add useful context;
- perform required cleanup.

Do not catch `Exception` merely to log and rethrow.

Preserve stack traces.

Do not expose internal or sensitive information to clients.

# Logging

Prefer structured logging.

Log meaningful events, not every method invocation.

Do not log secrets or complete sensitive upstream URLs.

Avoid high-volume logging inside item-processing loops unless specifically needed for diagnostics.

# Performance

Xtream payloads and catalogues can become large.

Pay attention to:

- database round trips;
- HTTP round trips;
- repeated enumeration;
- unnecessary materialization;
- JSON allocations;
- full-response buffering;
- expensive work inside per-item loops.

Prefer O(1) lookup structures such as dictionaries or sets when repeatedly resolving items by identifier.

Batch or preload data when that is simpler and avoids N+1 behavior.

Do not introduce caches without a demonstrated need and a clear invalidation strategy.

# Packages

NuGet versions are centrally managed in `Directory.Packages.props`.

Do not specify package versions in individual project files for centrally managed packages.

Before adding a package:

1. check whether the .NET platform already provides the capability;
2. check whether an existing package already provides it;
3. make sure the package solves a real problem;
4. add its version centrally.

Avoid adding dependencies for trivial functionality.

# Tests

Tests use xUnit v3 (`xunit.v3`) on Microsoft Testing Platform; `global.json` opts `dotnet test` into that runner.

Pass `TestContext.Current.CancellationToken` to methods that accept a `CancellationToken` (analyzer rule xUnit1051, enforced as an error).

Database-backed tests use SQLite in-memory through `SqliteDbContextFactory`; hold the context in the test class and dispose it through `IAsyncDisposable`. The factory stores `DateTimeOffset` values as binary numbers ordered by their UTC instant (`DateTimeOffsetToBinaryConverter`), so comparisons and ordering on dates translate as on PostgreSQL `timestamptz`. A concurrent write can be simulated with `BeforeFirstSaveInterceptor` and `SqliteDbContextFactory.CreateOnSameDatabase`.

Code depending on `HttpResponse.HasStarted` or `HttpContext.Abort()` is tested with `Infrastructure/ServerLikeHttpContext`: the features of `DefaultHttpContext` never report a started response.

Upstream Xtream and TMDB HTTP calls are replaced by the stub `IHttpClientFactory` implementations under `tests/XtreamForge.Tests/Infrastructure`.

The test project references `Aspire.Hosting.Testing`, but no Aspire integration test exists yet.

Routing behavior is tested in-process with a minimal `WebApplication` on `TestServer` (`Microsoft.AspNetCore.Mvc.Testing`), registering only the services the mapped routes need (see `Endpoints/XtreamRoutesTests`).

Write tests around observable behavior.

Prefer testing through public or meaningful internal boundaries rather than private methods.

Do not use reflection to test private implementation details.

Tests should be:

- deterministic;
- repeatable;
- focused;
- readable;
- independent of developer-machine state.

Do not weaken production code for the sole purpose of making testing easier.

Do not introduce a new mocking or assertion framework unless it provides clear value and is explicitly justified.

# Integration Tests

Use Aspire.Hosting.Testing when testing the distributed application requires real application resources.

Keep unit tests lightweight when infrastructure is unnecessary.

Do not turn every test into an Aspire integration test.

Conversely, do not heavily mock behavior whose correctness specifically depends on:

- ASP.NET Core routing;
- service discovery;
- PostgreSQL;
- EF Core relational behavior;
- actual HTTP integration.

Choose the smallest realistic test level that can validate the behavior.

# Validation

Before completing a code change:

1. build the affected project;
2. inspect all compiler/analyzer diagnostics;
3. run relevant tests;
4. expand to solution-wide build/tests when appropriate;
5. inspect the final diff.

For broad changes, use:

`dotnet build XtreamForge.slnx`

`dotnet test --solution XtreamForge.slnx`

A task is not complete merely because code was generated.

Do not claim that a build or test passed unless it was actually executed.

# Refactoring During the Rewrite

The repository is currently being simplified.

When refactoring existing code:

- preserve externally observable behavior unless the task changes it;
- reduce unnecessary concepts when safe;
- prefer fewer moving parts;
- keep responsibilities obvious;
- preserve security invariants;
- keep changes reviewable;
- use tests to protect important behavior.

Do not preserve complexity solely because it already exists.

At the same time, do not collapse genuinely distinct responsibilities into a single large class or method simply to reduce the number of files.

The target is conceptual simplicity, not minimum file count.

When choosing between two designs that satisfy the requirements, prefer the one that requires a future developer to understand fewer concepts.

# Documentation

`README.md` must always describe the current implementation.

Update `README.md` in the same task whenever a change affects projects, configuration keys or defaults, commands, routes, Xtream behavior, features, security behavior, or known limitations.

Verify every README statement against the source code; never document planned or assumed behavior as implemented, and correct or remove statements that became false.

Update this file and the skills under `.agents/skills` when a change introduces a new convention, component, or constraint relevant to future tasks.

`TECHNICAL_DEBT.md` lists known bugs, design issues, and improvement opportunities. Remove an entry in the same change that fixes it, update entries affected by a change, and add newly identified issues instead of leaving them undocumented.

Do not copy outdated architecture from README into new code.

Names in current project files and source code take precedence over stale documentation.

# Existing Work

The repository may contain in-progress rewrite work.

Treat existing uncommitted changes as user-owned.

Do not revert or overwrite unrelated work.

Do not perform destructive Git operations.

Do not commit, push, rebase, reset, or alter history unless explicitly requested.

# Definition of Done

A task is complete when:

1. the requested behavior works;
2. the implementation is no more complex than necessary;
3. project boundaries are respected;
4. Domain remains infrastructure-independent;
5. Web remains independent of Database;
6. security and credential-redaction requirements remain intact;
7. `.editorconfig` is respected;
8. the code builds with warnings-as-errors;
9. no new SonarAnalyzer.CSharp issue is introduced;
10. relevant tests pass;
11. no unrelated changes were introduced;
12. `README.md` and the AI documentation are updated when the change makes them inaccurate or incomplete.