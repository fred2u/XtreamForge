---
name: xtream-proxy
description: "Safely implement Xtream-compatible request forwarding and transformations while preserving HTTP semantics, credentials, streaming, and SSRF protections. Use whenever modifying Xtream proxy behavior in XtreamForge.ApiService/Endpoints/Xtream or XtreamForge.ApiService/Xtream."
---

# Purpose

Use this skill whenever modifying Xtream proxy behavior.

Relevant code is primarily under:

`XtreamForge.ApiService/Endpoints/Xtream`

and:

`XtreamForge.ApiService/Xtream`

# Current proxy architecture

Request path (read all components before modifying any one):

```
MapXtreamEndpoints()                    (RouteExtensions.cs: player_api.php, short live, prefixed forward, and account stream routes)

/{protocol}/{host}/{port}/player_api.php → HandlePlayerApiRequestAsync
  → XtreamContextBuilder.TryBuild()     (validates + assembles XtreamContext, 400 with the validation error otherwise)
  → XtreamProviderValidator             (SSRF/protocol/host/port guard)
  → dispatch on XtreamContext.Action:
      RequestAction.GetCategories       → CategoriesGetEndpoint
      RequestAction.GetItems            → ItemsGetEndpoint
      RequestAction.Authenticate        → AuthenticateEndpoint (GET, no action: server_info rewritten to XtreamForge, account upstream remembered in XtreamAccountDirectory)
      RequestAction.GetInfo             → ItemGetEndpoint (get_vod_info / get_series_info, rewritten via ItemService.TransformInfo)
      _                                 → XtreamRequestForwardEndpoint (transparent)

/{protocol}/{host}/{port}/{username}/{password}/{streamId} → ForwardShortLiveStreamAsync (short live form, forwarded like the catch-all route;
                                          its own route only names the credentials route values, which the telemetry redacts)

/{protocol}/{host}/{port}/{**rest}      → ForwardXtreamRequestAsync (streams and any other path)
  → XtreamContextBuilder.TryBuild()     (same validation)
  → XtreamRequestForwardEndpoint        (transparent; a GET of movie/{user}/{pass}/{id}.{ext} answered 2xx or 3xx is tracked by the singleton WatchHistoryQueue while it streams)

/{movie|series|live}/{username}/{password}/{file} → ForwardAccountStreamAsync (stream URLs built from the rewritten server_info)
  → XtreamAccountDirectory.Find()      (404 when the account has not authenticated since the start)
  → XtreamContextBuilder.TryBuild()     (same validation, rest = {kind}/{username}/{password}/{file})
  → XtreamRequestForwardEndpoint
```

The literal `player_api.php` route takes precedence over the catch-all route. Keep the forward route's handler limited to `XtreamContextBuilder` and `XtreamRequestForwardEndpoint`: stream requests are frequent and must not resolve services that create a `DbContext`. Minimal API handler parameters are resolved for every request of the route, so do not resolve services through `IServiceProvider` to work around this.

`XtreamContext` only carries the request: Protocol, Host, Port, Action, ContentType, Request, and Response. It holds no loaded data: `SourceService.GetSnapshotAsync` returns an `XtreamSourceSnapshot` (source ID, ordered enabled rules, TMDB mappings, deferred TMDB lookups) and `CategoryService.GetXtreamCategoryIdMappingAsync` returns the upstream-to-XtreamForge category mapping; pass them explicitly to the code that needs them.

Supported HTTP methods: GET, HEAD only. `XtreamHttpForwarder` therefore forwards no request body and drops content headers.

Every upstream Xtream call of the endpoints goes through `XtreamHttpForwarder.SendAsync` (upstream request built from the incoming one, sent with the Xtream HTTP client, response returned once its headers are read); a response forwarded unchanged (transparent forward, or a non-success status of a transformed action) is copied with `XtreamHttpForwarder.WriteResponseAsync`. This single class drops the hop-by-hop headers in both directions, and the client address headers of a reverse proxy (`X-Forwarded-*`, `X-Original-*`, `X-Real-IP`, `Forwarded`, ...) on the upstream request: providers may bind the stream URL they redirect to to the address they carry. It also marks the media stream requests (`XtreamStreamPath.IsStream`: `movie|series|live|timeshift/{username}/{password}/...` and the short live form) with `Infrastructure/StreamRequest`, so that they are sent at once and only once: neither paced nor retried by `RateLimitHandler`, nor retried by the standard resilience pipeline. A new stream path shape must be recognized by `XtreamStreamPath.IsStream`.

The proxy routes are excluded from OpenAPI (`ExcludeFromDescription`).

`XtreamCredentialRedaction` in `ServiceDefaults` is the credential-scrubbing mechanism — use it; do not implement a parallel one.

# Primary rule

XtreamForge is a proxy and transformation layer.

Preserve upstream HTTP behavior unless XtreamForge intentionally needs to transform it.

Do not alter transparent proxy behavior accidentally.

# Before changing proxy behavior

Inspect the relevant:

- endpoint;
- XtreamContext;
- XtreamContextBuilder;
- XtreamProviderValidator;
- XtreamHttpForwarder;
- application service;
- existing tests.

Understand the complete request path before modifying one component.

# Request preservation

When forwarding an upstream request, preserve as appropriate:

- HTTP method;
- query parameters;
- relevant end-to-end headers (never Host or hop-by-hop headers);
- cancellation.

Do not reconstruct a request differently without a reason.

# Response preservation

When transparently forwarding a response, preserve as appropriate:

- status code;
- relevant response headers;
- content headers;
- response body;
- streaming semantics.

Avoid buffering a full response when transformation is unnecessary.

# Transformations

Deserialize upstream responses only when XtreamForge needs to inspect or transform their contents.

When transforming JSON:

- preserve unknown/provider-specific fields where practical;
- change only fields owned by the transformation;
- do not unnecessarily normalize the provider payload.

# Security

Upstream destination information is untrusted.

Preserve validation of:

- protocol;
- hostname/IP;
- port;
- allowed destinations.

Never bypass `XtreamProviderValidator` or equivalent destination validation merely because a URL appears syntactically valid.

`XtreamProviderValidator.Validate` does not resolve host names: with `AllowAnyDestination`, the resolved addresses are checked by `XtreamProviderValidator.ConnectAsync`, the `SocketsHttpHandler.ConnectCallback` of the Xtream HTTP client, on the addresses actually connected to (DNS rebinding). Keep the Xtream client on this handler, with `UseProxy = false` (through a proxy, only the proxy address would be checked), and send every upstream Xtream request through this client.

Treat SSRF as a primary security concern.

# Credentials

Xtream credentials may be required for upstream forwarding.

They must not be exposed through:

- logs;
- exception messages;
- telemetry;
- traces;
- metrics;
- diagnostic URLs.

Use the existing credential-redaction mechanisms: `XtreamCredentialRedaction` (query parameters and stream path credentials, see `RedactPath`) and the `XtreamHttpClientLogger` of the Xtream HTTP client, which replaces the default `IHttpClientFactory` logging. A new stream path kind carrying credentials must be added to the stream path pattern of `XtreamCredentialRedaction`. Credentials without a recognizable path shape must be route values named `username` / `password`: `RedactServerResponse` redacts them from the server spans once routed, and `XtreamHttpForwarder` marks them on the upstream request (`SetPathCredentials`) for the client spans and logs (`RedactRequestUri`). The ASP.NET Core request logs (`Microsoft.AspNetCore.Hosting.Diagnostics`) stay disabled: they and their `RequestPath` log scope write the raw path.

Never persist upstream credentials as source identity unless explicitly designed and security-reviewed.

# Upstream failures

The Xtream endpoints translate upstream failures with `XtreamUpstreamFailure` (`IsUpstreamFailure` as exception filter, then `Handle`): 504 for a timeout, 502 for a network error, an interrupted body (`IOException`), or invalid JSON, and an aborted connection once the response has started. Start a streamed response explicitly (`Response.StartAsync`, with its `Content-Type`) before writing to it. `XtreamHttpForwarder.WriteResponseAsync` copies the body unchanged and keeps `Content-Length` (null for a chunked or decompressed upstream body).

# HttpClient

Use the configured/injected HTTP client infrastructure.

Do not instantiate ad-hoc HttpClient objects in request processing.

Propagate cancellation.

Dispose request and response resources correctly without prematurely disposing streams that are still being written to the client.

# Performance

Proxy paths may process large responses.

Prefer streaming where possible.

Avoid:

- unnecessary copies;
- multiple serialization passes;
- per-item network requests;
- per-item database queries;
- unnecessary buffering.

# Validation

For proxy changes, test at least:

- expected normal forwarding;
- transformed behavior if applicable;
- malformed/invalid upstream destination;
- credential-redaction behavior when relevant;
- cancellation or streaming behavior when relevant.

Never weaken security validation to make a test pass.
