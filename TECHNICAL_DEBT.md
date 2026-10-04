# Technical debt

Known bugs, design issues, and improvement opportunities identified in the current implementation.

Remove an entry in the same change that fixes it. Keep the remaining entries accurate when the related code changes.

## Security

### Credentials of short live stream paths not redacted

`XtreamCredentialRedaction.RedactPath` recognizes the stream paths by their kind (`movie|series|live|timeshift/{username}/{password}/...`). The short live form `{username}/{password}/{streamId}`, forwarded by the prefixed route (`/{protocol}/{host}/{port}/{username}/{password}/{id}`), has no kind segment: its credentials stay in the spans and in the Xtream HTTP client logs. The ASP.NET Core request logs (`Microsoft.AspNetCore.Hosting.Diagnostics`, `Information`) also write the request path unredacted; they are only silenced by the `Microsoft.AspNetCore: Warning` level of `appsettings.json`.

Fix: redact the path of the prefixed forward route from its route values rather than from its shape, and filter the ASP.NET Core request logs.

### Admin API without authentication, exposed with the proxy

`/api/admin/...` has no authentication or authorization and is served by the same host and port as the Xtream proxy, which must be reachable by the IPTV clients: anyone able to use the proxy can read and change the configuration (sources, rules, mappings, TMDB exclusions, watch history).

Fix: map the admin endpoints in a `MapGroup("/api/admin")` to apply an authorization policy (or serve them on a separate, non-exposed endpoint), and authenticate the Web UI.

### SSRF validation open to DNS rebinding

`XtreamProviderValidator` resolves the host when the request is validated (cached 5 minutes in a static, unbounded dictionary), but `HttpClient` resolves it again when connecting: a host can resolve to a public address during validation and to a private one at connection time. The resolution is also synchronous (`Dns.GetHostAddresses`) on the request path, failures are swallowed by a bare `catch`, and some non-public IPv4 ranges are allowed (224.0.0.0/4 multicast, 240.0.0.0/4 reserved, 198.18.0.0/15 benchmarking, 192.0.0.0/24).

Fix: validate the address actually connected to in the `SocketsHttpHandler.ConnectCallback` of the Xtream client, block the missing ranges, and bound or remove the static cache.

## Bugs

### Concurrent category synchronization fails

`CategoryService.SyncCategoriesAsync` reads the source, then inserts it (or its new categories) without handling a concurrent insert. Clients often request `get_vod_categories` and `get_series_categories` in parallel on startup: for a new source, both requests insert it and the second fails on the unique `(protocol, host, port)` index with an unhandled `DbUpdateException` (HTTP 500). The same race exists for a new category on the `(xtream_source_id, content_type, xtream_id)` index. No `DbUpdateException` is handled anywhere in the solution.

Fix: create the source with an upsert (`INSERT ... ON CONFLICT DO NOTHING`) or retry the synchronization once on a unique violation.

### Catalogue lists without Content-Type

`ItemsGetEndpoint` writes the `get_vod_streams` / `get_series` response directly to `Response.BodyWriter` without setting `Content-Type`. Response compression only applies to the configured MIME types, so the largest Xtream payloads are never compressed, and clients receive no media type.

Fix: set `Response.ContentType` to `application/json; charset=utf-8` before writing.

### Errors after the response has started

`ItemsGetEndpoint` (streamed list) and `XtreamRequestForwardEndpoint` (streamed body) catch `JsonException`, `HttpRequestException`, and timeouts by returning a 502 / 504 result. When the error happens after the headers were sent, the status cannot change: setting it throws `InvalidOperationException`, and the client receives a truncated body (invalid JSON for the lists). An `IOException` raised while copying the upstream body is not caught.

Fix: when `Response.HasStarted`, log and abort the connection (`HttpContext.Abort()`) instead of returning a status result.

### Content-Length dropped from forwarded responses

`XtreamHttpResponseMessageWriter` copies every content header except `Content-Length`: forwarded streams are always sent chunked, and `HEAD` and `206` responses lose their length, which some players use to show the duration and to seek.

Fix: forward `Content-Length` when the body is copied unchanged.

### Dockerfiles do not build

`src/XtreamForge.ApiService/Dockerfile` restores and publishes `src/XtreamForge/XtreamForge.csproj`, which does not exist, and does not copy the Domain and Database projects. Neither Dockerfile copies `Directory.Packages.props`, so the restore fails with central package management.

Fix: restore the actual project files with `Directory.Packages.props`, or remove the Dockerfiles and rely on Aspire publishing.

## Design

### Catalogue can stay empty

Items are returned only when a TMDB ID is known and its TMDB metadata (`TmdbInfo`) is loaded. Without `Tmdb:ApiKey`, no metadata is ever loaded, so no item is ever returned, even when the provider exposes `tmdb_id`; the first catalogue requests are mostly empty until the background lookups and loads catch up.

Fix: decide on a degraded mode (for example an option to return items without a TMDB ID or without TMDB metadata).

### Catalogue latency depends on TMDB

The virtual categories are computed on the Xtream request path (`TmdbIdCache.GetOrComputeAsync`): when the cache is empty, a catalogue request with all the categories waits for 20 TMDB recommendation calls and 5 popular pages, one computation at a time. A TMDB failure is not cached, so while TMDB is down every such request calls it again and waits for the resilience timeouts before the catalogue is returned.

Fix: cache failures for a short time, or refresh the sets in the background and serve the last known value.

### Transient failures defer lookups for days

`TmdbIdRetrieverService` and `TmdbInfoService` defer the next attempt (1 day, doubling up to 30 days) after any exception, as after a lookup without result: a provider or TMDB outage, a timeout, or an HTTP 429 after the three attempts hides the affected items for at least one day.

Fix: only defer on a definitive result (not found, not scorable); retry transient failures (network, timeout, 429, 5xx) after a short delay.

### Reverse proxy headers ignored

`AuthenticateEndpoint` rewrites `server_info` from `Request.Host`, `Request.Scheme`, and the request port, but the forwarded headers middleware is not configured: behind a reverse proxy terminating TLS, clients receive `http` and the internal port.

Fix: configure `UseForwardedHeaders` (`X-Forwarded-Proto`, `X-Forwarded-Host`) with the known proxies.

### Proxy not exposed outside Aspire development

In `AppHost.cs`, only the Web project has `WithExternalHttpEndpoints`; the ApiService, which serves the Xtream proxy to the IPTV devices, is not marked as external, and no deployment path is documented (see the Dockerfiles above).

Fix: decide how the proxy is exposed (external endpoint, reverse proxy) together with the admin API isolation, and document it in `README.md`.

### Incomplete options validation

`XtreamProxyOptions` is not validated at startup (`AllowAnyDestination` false with an empty `AllowedHosts`, the `appsettings.json` default, rejects every upstream without warning), and nothing prevents `Recommendations:CategoryId` and `Popular:CategoryId` from being equal.

Fix: validate both cases with `ValidateOnStart`, or at least log a startup warning for an empty allowlist.

### Migrations applied at startup

`UseDatabase` runs `Database.Migrate()` at startup; several API instances would migrate concurrently.

Fix: acceptable for a single instance; move migrations to a dedicated step if the API is scaled out.

### Watch history only records movies

Series episodes are not recorded: the episode stream URL (`series/{username}/{password}/{episodeId}.{ext}`) only carries the provider episode ID, while TMDB mappings are keyed by series ID, and no Xtream action resolves an episode to its series. A playback is also recorded from its first request, whatever the part actually watched.

Fix: map episodes to their series (for example from the `get_series_info` payloads seen by `ItemGetEndpoint`), and, if needed, estimate the watched part from the byte ranges served for a playback.

### Application enums in Domain

`RequestAction` (Xtream request classification), `XtreamCategoryPatchResult`, and `RuleReorderResult` (admin service results) are application concerns stored in `XtreamForge.Domain/Enums`.

Fix: move them next to the ApiService code using them.

### Duplicated Xtream error handling

`AuthenticateEndpoint`, `CategoriesGetEndpoint`, `ItemsGetEndpoint`, `ItemGetEndpoint`, and `XtreamRequestForwardEndpoint` repeat the same `try/catch` translating timeouts, `HttpRequestException`, and `JsonException` into 504 / 502, with the same context-free message (`"ErrorMessage: {ErrorMessage}"`).

Fix: share one helper (which can also handle the started-response case above) and log the action and upstream host.

### Inconsistent comparisons and clock

`CategoryService` compares `xtream_id` ignoring case in memory while the PostgreSQL unique index is case-sensitive, and uses `DateTimeOffset.UtcNow` where the other services use the injected `TimeProvider`.

Fix: use one comparison for provider IDs in memory and in the database, and inject `TimeProvider`.

## Performance

### Watch activity reads the whole history

`WatchHistoryAdminService.GetActivityAsync` loads the start date of every movie playback ever recorded and keeps the last 371 days in memory: the query has no date filter, because SQLite tests cannot compare `DateTimeOffset`. An index on `started_at_utc` would only help once the query filters on it.

Fix: filter server-side from the UTC start of the first day (minus one day for the time zone offsets) and add the index, with a test strategy supporting `DateTimeOffset` comparisons.
