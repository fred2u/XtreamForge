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
MapXtreamEndpoints()                    (RouteExtensions.cs: player_api.php, prefixed forward, and account stream routes)

/{protocol}/{host}/{port}/player_api.php → HandlePlayerApiRequestAsync
  → XtreamContextBuilder.Build()        (validates + assembles XtreamContext)
  → XtreamProviderValidator             (SSRF/protocol/host/port guard)
  → dispatch on XtreamContext.Action:
      RequestAction.GetCategories       → CategoriesGetEndpoint
      RequestAction.GetItems            → ItemsGetEndpoint
      RequestAction.Authenticate        → AuthenticateEndpoint (GET, no action: server_info rewritten to XtreamForge, account upstream remembered in XtreamAccountDirectory)
      RequestAction.GetInfo             → ItemGetEndpoint (get_vod_info / get_series_info, rewritten via ItemService.TransformInfo)
      _                                 → XtreamRequestForwardEndpoint (transparent)

/{protocol}/{host}/{port}/{**rest}      → ForwardXtreamRequestAsync (streams and any other path)
  → XtreamContextBuilder.Build()        (same validation)
  → XtreamRequestForwardEndpoint        (transparent; a GET of movie/{user}/{pass}/{id}.{ext} answered 2xx or 3xx is tracked by the singleton WatchHistoryQueue while it streams)

/{movie|series|live}/{username}/{password}/{file} → ForwardAccountStreamAsync (stream URLs built from the rewritten server_info)
  → XtreamAccountDirectory.Find()      (404 when the account has not authenticated since the start)
  → XtreamContextBuilder.Build()        (same validation, rest = {kind}/{username}/{password}/{file})
  → XtreamRequestForwardEndpoint
```

The literal `player_api.php` route takes precedence over the catch-all route. Keep the forward route's handler limited to `XtreamContextBuilder` and `XtreamRequestForwardEndpoint`: stream requests are frequent and must not resolve services that create a `DbContext`. Minimal API handler parameters are resolved for every request of the route, so do not resolve services through `IServiceProvider` to work around this.

`XtreamContext` only carries the request: Protocol, Host, Port, Action, ContentType, Request, and Response. It holds no loaded data: `SourceService.GetSnapshotAsync` returns an `XtreamSourceSnapshot` (source ID, ordered enabled rules, TMDB mappings, deferred TMDB lookups) and `CategoryService.GetXtreamCategoryIdMappingAsync` returns the upstream-to-XtreamForge category mapping; pass them explicitly to the code that needs them.

Supported HTTP methods: GET, HEAD only. `XtreamHttpRequestMessageFactory` therefore forwards no request body and drops content headers.

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
- XtreamHttpRequestMessageFactory;
- XtreamHttpResponseMessageWriter;
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

Use the existing credential-redaction mechanisms.

Never persist upstream credentials as source identity unless explicitly designed and security-reviewed.

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
