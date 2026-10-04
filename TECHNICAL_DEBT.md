# Technical debt

Known bugs, design issues, and improvement opportunities identified in the current implementation.

Remove an entry in the same change that fixes it. Keep the remaining entries accurate when the related code changes.

## Security

### Admin API without authentication, exposed with the proxy

`/api/admin/...` has no authentication or authorization and is served by the same host and port as the Xtream proxy, which must be reachable by the IPTV clients: anyone able to use the proxy can read and change the configuration (sources, rules, mappings, TMDB exclusions, watch history).

Fix: map the admin endpoints in a `MapGroup("/api/admin")` to apply an authorization policy (or serve them on a separate, non-exposed endpoint), and authenticate the Web UI.

### SSRF validation open to DNS rebinding

`XtreamProviderValidator` resolves the host when the request is validated (cached 5 minutes in a static, unbounded dictionary), but `HttpClient` resolves it again when connecting: a host can resolve to a public address during validation and to a private one at connection time. The resolution is also synchronous (`Dns.GetHostAddresses`) on the request path, failures are swallowed by a bare `catch`, and some non-public IPv4 ranges are allowed (224.0.0.0/4 multicast, 240.0.0.0/4 reserved, 198.18.0.0/15 benchmarking, 192.0.0.0/24).

Fix: validate the address actually connected to in the `SocketsHttpHandler.ConnectCallback` of the Xtream client, block the missing ranges, and bound or remove the static cache.

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

In `AppHost.cs`, only the Web project has `WithExternalHttpEndpoints`; the ApiService, which serves the Xtream proxy to the IPTV devices, is not marked as external, and no deployment path is documented (the Dockerfiles under `src/` build the ApiService and Web images, but nothing describes how to run them).

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

### Inconsistent comparisons and clock

`CategoryService` compares `xtream_id` ignoring case in memory while the PostgreSQL unique index is case-sensitive, and uses `DateTimeOffset.UtcNow` where the other services use the injected `TimeProvider`.

Fix: use one comparison for provider IDs in memory and in the database, and inject `TimeProvider`.
