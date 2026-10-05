---
name: blazor-feature
description: "Implement XtreamForge administration features in the Blazor Web project using feature-oriented organization and Fluent UI. Use when creating or changing UI functionality in XtreamForge.Web."
---

# Purpose

Use this skill when creating or changing UI functionality in `XtreamForge.Web`.

The Web project is a presentation application.

Backend business logic belongs in ApiService.

Persistence belongs in Database.

# Project layout (current)

```
src/XtreamForge.Web/
  Components/            # App shell, routes, layout
    Shared/              # PageHeader, EmptyState, CountBadge, SegmentedControl, LoadErrorState + ApiErrorMessage, LineChart (SVG, ChartSeries + ChartGeometry) (generic UI building blocks only)
  Features/
    Dashboard/           # DashboardPage.razor (/): status + counters grouped by DashboardMetricGroup (configuration, TMDB) + DashboardClient.cs + MetricContent
    History/             # WatchHistoryPage (/history): playbacks most recent first (FluentDataGrid ItemsProvider + Virtualize), WatchHistoryClient.cs, WatchHistoryModels.cs
    Monitoring/          # MonitoringPage (/monitoring) + MonitoringClient.cs + QueueMonitorPanel (background queues and upstream rate limits, polled every 5 s; one row per queue: summary + two charts on a shared column template, rate limits in a table; responsive through container queries on `.monitor-section`)
    Sources/             # SourcesPage (/sources): list with per-source and per-content-type counters (categories from the provider, effective after filtering, excluded manually / by provider / by rule, mapped, rules, TMDB StreamId→TmdbId mappings) linking to the filtered category and rule screens (`SourceContentFacts`), create (URL + credentials, not stored), delete; SourcesClient.cs (XtreamSourceSummaryDto with per-source counters)
    Categories/          # XtreamCategoriesPage (/categories/xtream), CustomCategoriesPage (/categories/custom), CategoryRulesPage (/categories/rules),
                         # CategoryScopeBar (source + content type selection), CategoryDecisionBadge/CategoryDecisionDetails (effective state and reason),
                         # CategoriesClient.cs, CategoryModels.cs, pure UI logic (XtreamCategoryFilter, RuleOrdering) tested in tests/XtreamForge.Tests/Web
    Items/               # ItemRulesPage (/items/rules)
    Tmdb/                # TmdbInfosPage (/tmdb/infos): server-side filtered and paged grid (FluentDataGrid ItemsProvider + Virtualize, RefreshDataAsync on filter change),
                         # title cell with poster thumbnail, genre chips (one line, max 3), rating with votes, manual exclusion, Genre filter (genres
                         # returned with each page); a row click opens the TmdbInfoDetails dialog (manual exclusion switch inside); TmdbRulesPage (/tmdb/rules);
                         # TmdbInfosClient.cs, TmdbModels.cs (TmdbInfoFilter.ToQueryString), TmdbScreenState
    Rules/               # RuleEditorDialog (create/edit modal shared by RulesManager and the Xtream categories page, `ShowAsync` + `Saved`), RulesManager: rules screen shared by category, item, and TMDB rules (RuleKind selects the endpoints and RuleKindTexts; RuleKind.Tmdb is global: no source, content type only, and a Title/Genre field)
  Configuration/         # BackendApiServiceCollectionExtensions, BackendOptions
  Program.cs
  _Imports.razor
```

Follow this layout. New features go under `Features/<FeatureName>/`.

The Web project does not reference ApiService or Domain: request/response records and enums are redeclared in the feature (for example `Features/Categories/CategoryModels.cs`). ApiService serializes enums as numbers, so Web enums must keep the same numeric values as `XtreamForge.Domain.Enums`.

Feature clients return `AdminOperationResult` (`Success`, `NotFound`, `Conflict`, `Invalid` for 400) for mutations and throw on other failures; components show the outcome with `INotificationService` toasts (`ShowSuccessToastAsync`, ...). Destructive actions are confirmed with `IDialogService.ShowConfirmationAsync` (check `result.Cancelled`). `<FluentProviders />` at the end of `MainLayout` provides toasts, dialogs and tooltips.

# Fluent UI v5 notes

The Web project uses Fluent UI Blazor 5 (web components based). Pitfalls met so far:

- `FluentTextInput` replaces `FluentTextField`/`FluentSearch` (search icon in `StartTemplate`); `FluentSelect` takes two type parameters (`TOption`, `TValue`) and binds `Value`.
- Enums are specific per component: `ButtonAppearance`, `BadgeColor`, `TextInputAppearance`, ...; icons inherit `currentColor` (no `Color.Custom`).
- `FluentButton.Label` renders visible text: icon-only buttons use `IconOnly="true"`, an `aria-label` attribute and `Tooltip`.
- A field `Message` is only shown with a `MessageCondition`, for example `MessageCondition="@(field => !string.IsNullOrEmpty(field.Message))"`.
- Theme: `IThemeService.SetThemeAsync(ThemeMode)` persists the choice in `localStorage` (`fluentui-blazor:theme-settings`, `mode` = `light`/`dark`, absent = system); `App.razor` applies it before Blazor starts to avoid a flash.
- The library injects `default-fuib.css` at runtime, after `app.css`: element selectors such as `h1` must be scoped (`.content-frame h1`) to win.
- `FluentSortableList` bundles SortableJS and supports keyboard reordering; with `Handle="true"` the handle has the class `sortable-grab`.
- `FluentDataGrid` pads every cell by 18px (`overflow: hidden`, ellipsis) and, with `Virtualize`, gives its header the `ItemSize` height as an inline style; it has no header height parameter.
- `FluentDataGrid.OnRowClick` receives a `FluentDataGridRow<TGridItem>` (`Item` is null for the header row); Enter on a focused cell also raises it. Controls inside a clickable row stop the propagation (`@onclick:stopPropagation="true"` on their wrapper).
- `FluentDialog Modal="false"` renders a `type="modal"` dialog (backdrop, closed by Escape or a click outside); the default and `Modal="true"` render `type="alert"`, which a click outside does not close.

# UI conventions

- Pages start with `<PageHeader Title Subtitle>` and put page-level actions (for example Refresh) in `Actions`.
- Use `EmptyState` for empty content (with a call to action when useful), `LoadErrorState` for failed loads (user-friendly message from `ApiErrorMessage`, technical details collapsed, Retry) and `FluentSkeleton` placeholders while loading.
- Charts use the shared `LineChart` (no chart library): series colors come from `chart-series-brand|success|neutral|danger` classes setting `--chart-series-color`. `LineChart` takes a `Title` shown in a fixed-height header with the legend, and uses a fixed grid (fixed-width Y axis, labels centered on the grid lines, X axis under the plot only) sized by `--line-chart-header-height`, `--line-chart-plot-height` and `--line-chart-axis-width`: keep these shared by the charts and the content displayed next to them so that everything stays aligned. Live data is polled with a `PeriodicTimer` started in `OnAfterRender(firstRender)` (not during prerendering) and disposed with the component.
- Light, dark and system themes are supported. In `wwwroot/app.css`, style with Fluent v5 design tokens (`--colorNeutralBackground*`, `--colorNeutralForeground*`, `--colorBrand*`, `--colorStatus*`) or the `--xf-*` variables; never hard-code light-only colors. Status colors use `.status-pill` + `status-pill-success|warning|danger|accent|neutral`.
- Keep navigation state that users may want to reload or share in the query string (`[SupplyParameterFromQuery]` + `NavigationManager.GetUriWithQueryParameters` with `replace: true`), as the category pages do with `source` and `type`. The Xtream categories page also accepts `decision`, `manual`, `provider`, `mapping` (filter enum names) and `search`, used by links from the sources screen. The TMDB infos page accepts `type`, `search`, `decision`, `manual`, `load` (`TmdbInfoFilter.FromLink`), used by links from the dashboard; a link with a filter parameter resets the absent filters, and the filter parameters are removed from the URL once applied.
- Category screens are scoped: the user explicitly picks a source, then a content type (`CategoryScopeBar`); there is no "all sources" or "all content types" choice, and only `Vod` and `Series` are exposed. Search and filters are enabled once the scope is chosen.
- Custom interactive elements (segmented controls, filter chips) are native `<button>` elements with `aria-pressed`/`aria-checked`, so they remain keyboard accessible. Use `SegmentedControl` for small option sets.
- Selects rendered in every row of a large grid are native `<select class="xf-select">` (much lighter than `FluentSelect`); large grids use `FluentDataGrid Virtualize="true"` inside a `.grid-viewport`. Lists that can reach tens of thousands of rows (TMDB infos) are filtered and paged by the API through `ItemsProvider`.
- Grid layout: `app.css` aligns the header labels (plain and sortable) on the cell padding and fixes the header height to 48px in every `.grid-viewport`. Wrap a `FluentSwitch` cell in `<div class="switch-cell">` so it is vertically centered. Choose `GridTemplateColumns` minimums (`minmax(...)`, the 36px cell padding included) whose sum fits a 1100px wide window (sidebar included), to avoid a horizontal scrollbar; prefer a secondary line and `cell-truncate` over wide columns.
- Details of a grid row (Xtream categories, TMDB infos): a click anywhere on the row (tags included) opens a `FluentDialog Modal="false"` showing the decision, the deciding rule (`RuleSentence`, priority, link to the rules screen), the per-item settings in `.details-controls` (one `.details-control` per setting: label and hint, then the control) and the facts (`.rule-facts`); the row name is the dialog title. No dialog opens from another dialog (for example, no "New custom category" in the details).
- Create/edit forms open in a modal `FluentDialog` declared in the page (`@ref` + `ShowAsync`/`HideAsync`, `FluentDialogBody` with `TitleTemplate`, `ChildContent` and footer buttons in `ActionTemplate`); `OnStateChange` with `DialogState.Closed` resets the editor (Escape), `PreventDismissOnEscape` is set while saving. Never insert an inline editor zone in the page.
- Category screens keep their filters in the scoped `CategoryScreenState` so they survive navigation; without a query-string scope they default to the first source and the first content type.
- No bulk operations in the admin UI: mutations are per item.
- Drag & drop uses `FluentSortableList` and always has a visible keyboard alternative (move up/down buttons).
- The UI never evaluates category rules: it displays the decision computed by the API.

# Admin API contracts

The Web project talks to `XtreamForge.ApiService` via HTTP. Endpoints are declared in `XtreamForge.ApiService/Endpoints/Admin/RouteExtensions.cs`; currently consumed by Web:

- `GET /api/admin/status` → `DashboardStatusDto` (application/database status and counters: sources, Xtream and custom categories, category/item/TMDB rules, known and unresolved TMDB mappings, TMDB infos total/not loaded/manually excluded; zero when the database is unavailable)
- `GET /api/admin/queues` → `QueuesStatusDto`, read by `MonitoringClient` (`SampleInterval`, per queue `Name`, `Size` and `Samples` oldest first with `At`, `Size`, `Succeeded`, `NoResult`, `Failed` since the previous sample; `RateLimitedHosts` with `Host`, `Interval`, `RateLimitedCount`)
- `GET /api/admin/sources` → `XtreamSourceSummaryDto[]` (id, protocol, host, port + `Vod` and `Series` `XtreamSourceContentSummaryDto`: categories, effective, manually excluded, provider-disabled, rule-excluded, custom-mapped, category rules, item rules, TMDB mappings; effective counts are computed with `CategoryRuleService.Evaluate`; a superset of the fields Categories reads as `XtreamSourceDto`)
- `POST /api/admin/sources` with `{ url, username, password }`: validates the URL with `XtreamProviderValidator` (400), 409 when the protocol/host/port already exists, discovers the VOD and series categories with the credentials (never stored) and returns 502 without creating anything when the provider fails; 201 + `XtreamSourceDto` otherwise
- `DELETE /api/admin/sources/{id}`: cascades to Xtream categories, rules and TMDB mappings; custom categories are kept
- `GET|POST /api/admin/custom-categories`, `PUT|DELETE /api/admin/custom-categories/{id}`
- `GET /api/admin/sources/{sourceId}/xtream-categories?contentType=` (each category includes `Decision`, `ExclusionReason` = `ManuallyExcluded`/`ProviderDisabled`/`Rule` and `DecidingRule`, computed with `CategoryRuleService.Evaluate`)
- `PATCH /api/admin/xtream-categories/{id}` with `{ isExcluded?, customCategoryId?, unassignCustomCategory }`: null leaves a value unchanged, `unassignCustomCategory: true` removes the mapping; a non-positive id, assign + unassign, or an unknown custom category → 400 validation problem; unknown category → 404
- `GET|POST /api/admin/sources/{sourceId}/category-rules`, `PUT|DELETE /api/admin/category-rules/{id}` (lower `Sequence` = higher priority, unique per source + content type → 409; an undefined content type, action, or operator, or a blank or too long pattern → 400)
- `PUT /api/admin/sources/{sourceId}/category-rules/order` with `{ contentType, ruleIds }`: atomic reorder; `ruleIds` must list every rule of the source and content type exactly once (else 400); sequences become 10, 20, 30, ... and the reordered rules are returned
- `GET|POST /api/admin/sources/{sourceId}/item-rules`, `PUT|DELETE /api/admin/item-rules/{id}`, `PUT /api/admin/sources/{sourceId}/item-rules/order`: same contracts as the category rules; Web calls both through `CategoriesClient.*RuleAsync(RuleKind, ...)`
- `GET /api/admin/tmdb-rules?contentType=`, `POST /api/admin/tmdb-rules`, `PUT|DELETE /api/admin/tmdb-rules/{id}`, `PUT /api/admin/tmdb-rules/order`: TMDB rules, global per content type (no source, `XtreamSourceId` is null in `RuleDto`), with a required `field` (`Title` = 1, `Genre` = 2); also called through `CategoriesClient.*RuleAsync(RuleKind.Tmdb, null, ...)`
- `GET /api/admin/tmdb-infos?contentType=&search=&genre=&decision=&isExcluded=&isLoaded=&skip=&take=` → `{ items, matchingCount, totalCount, excludedCount, manuallyExcludedCount, notLoadedCount, genres }` (each item has `decision`, `exclusionReason` = `ManuallyExcluded`/`Rule`, `decidingRule`, poster URLs `w92`/`w342`); `GET /api/admin/tmdb-infos/{id}` → details (overview, directors, cast, duration); `PATCH /api/admin/tmdb-infos/{id}` with `{ isExcluded }`
- `GET /api/admin/watch-history?contentType=&skip=&take=` → `{ items, matchingCount }` (most recent first; each item has `contentType`, `tmdbId`, `startedAtUtc`, TMDB title, original title, release date, `w92` poster URL)

List endpoints are scoped by source and/or content type. Deleting a custom category still mapped by Xtream categories fails with 500 (FK `Restrict`), so the UI checks mappings first (`CategoriesClient.GetCustomCategoryMappingsAsync`, one call per source and content type).

When a new feature needs backend data, add or extend an endpoint in `ApiService` first, then consume it from a feature client in Web.

# Feature organization

Keep feature-specific code together under:

`XtreamForge.Web/Features/<Feature>`

A feature may contain:

- Razor pages/components;
- HTTP clients;
- presentation models;
- UI-specific state;
- small feature-specific helpers.

Prefer feature cohesion over global folders organized only by technical type.

# Backend communication

Web communicates with ApiService through HTTP.

Do not:

- reference `XtreamForge.Database`;
- use `XtreamForgeDbContext`;
- connect directly to PostgreSQL;
- duplicate backend rules to avoid an API call.

When the UI needs new backend behavior, add or evolve an appropriate ApiService endpoint.

# Service discovery

Use the existing configured HttpClient/service discovery mechanism.

Do not hard-code development URLs or Aspire ports in feature code.

# HttpClient registration pattern

Typed HTTP clients are registered in `BackendApiServiceCollectionExtensions.cs`.

When adding a new feature client:

1. Create `Features/<Feature>/<Feature>Client.cs` with a typed `HttpClient` constructor.
2. Register it in `BackendApiServiceCollectionExtensions.AddBackendApiClients()`:
   ```csharp
   services.AddHttpClient<MyFeatureClient>(ConfigureBackendClient);
   ```
3. The base URL comes from `BackendOptions` (bound from `Backend:BaseUrl`) — already wired via `ConfigureBackendClient`.
4. Inject the client into the Razor component via `@inject MyFeatureClient Client`.

Do not create `HttpClient` instances directly or call `services.AddHttpClient()` with a factory outside `AddBackendApiClients`.

# Fluent UI

Use Microsoft Fluent UI Blazor components when they adequately solve the UI requirement.

Follow existing visual and component conventions.

Do not introduce an additional UI component framework for isolated convenience.

# Component design

Keep components focused on presentation and interaction.

Avoid large Razor components containing:

- substantial business logic;
- database logic;
- complex HTTP orchestration;
- unrelated feature responsibilities.

Extract code when doing so improves clarity, not merely to reduce line count.

# State

Prefer explicit component/feature state.

Make loading, success, empty, and error states clear where applicable.

Prevent duplicate submissions when an operation is already running when relevant.

Do not rely on full browser reloads when Blazor state can be updated normally.

# Async

Use asynchronous event handlers for I/O.

Handle cancellation/disposal appropriately for component lifetime when long-running operations are involved.

Avoid blocking calls.

# Validation

After changing a Blazor feature:

- build `XtreamForge.Web`;
- validate API contracts used by the feature;
- exercise relevant component behavior;
- run related tests;
- check for analyzer and Sonar diagnostics.
