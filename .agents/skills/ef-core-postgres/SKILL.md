---
name: ef-core-postgres
description: "Implement and modify XtreamForge persistence using EF Core, Npgsql, PostgreSQL, entity mappings, and migrations. Use for work involving XtreamForgeDbContext, entities, EF Core queries, mappings, constraints, indexes, migrations, or persistence performance."
---

# Purpose

Use this skill for work involving:

- XtreamForgeDbContext;
- entities persisted in PostgreSQL;
- EF Core queries;
- entity mappings;
- database constraints;
- indexes;
- migrations;
- persistence performance.

Persistence belongs in `XtreamForge.Database`.

Domain concepts belong in `XtreamForge.Domain`.

# Before changing persistence

Inspect:

1. the domain entity;
2. `XtreamForgeDbContext`;
3. the applicable mapping under `XtreamForge.Database/Mappings`;
4. related migrations;
5. existing service/query usage;
6. relevant tests.

Follow existing repository conventions instead of inventing a new persistence pattern.

# Current schema overview

`XtreamForgeDbContext` exposes:

- `XtreamSources` (`XtreamSource`) — upstream source identity (Protocol + Host + Port)
- `XtreamCategories` (`XtreamCategory`) — discovered upstream categories, scoped by source + ContentType
- `CustomCategories` (`CustomCategory`) — global custom categories, scoped by ContentType
- `CategoryRules` (`CategoryRule`) — ordered rules per source + ContentType
- `ItemRules` (`ItemRule`) — ordered item rules per source + ContentType
- `StreamTmdbMappings` (`StreamTmdbMapping`) — `Source + ContentType + StreamId -> TmdbId`; a null `TmdbId` is a lookup without result, retried from `NextLookupAtUtc`
- `TmdbInfos` (`TmdbInfo`) — TMDB metadata per `ContentType + TmdbId`, with its load state (`LoadedAtUtc`, `LoadAttemptCount`, `NextLoadAtUtc`); every metadata value is nullable; `GenreIds`, `Genres`, `Directors`, and `Cast` (column `cast_members`, `cast` being a PostgreSQL reserved word) are PostgreSQL arrays (`integer[]` / `text[]`, JSON in SQLite tests), never null; a migration adding a non-nullable array column to an existing table needs `defaultValueSql: "'{}'"`; `IsExcluded` is the manual exclusion
- `SeriesEpisodes` (`SeriesEpisode`) - `Source + EpisodeId -> SeriesId` (unique), with the season and episode numbers, persisted from the `get_series_info` payloads so that an episode stream (which only carries the episode ID) can be resolved to its series
- `WatchHistory` (`WatchHistoryEntry`) - one row per movie or series episode playback (`ContentType`, `TmdbId` of the movie or series, nullable `SeasonNumber` / `EpisodeNumber`, `StartedAtUtc`, nullable `XtreamSourceId`), global (no account; the source is informative only, null for a playback added by the admin, and set to null when the source is deleted); listed by descending `Id`, which follows the start order; indexed on `(ContentType, TmdbId)` (recommendation seeds) and `(ContentType, StartedAtUtc)` (activity of the last year)
- `TmdbRules` (`TmdbRule`) - global TMDB rules per content type (unique `ContentType + Sequence`), on the TMDB `Title` or `Genre`

Migrations live under `XtreamForge.Database/Migrations/`.
The `DesignTimeDbContextFactory` is used for `dotnet ef` tooling.

Source identity is `(Protocol, Host, Port)` — credentials are never used as the source key and must not be persisted.

# EF Core usage

Use EF Core directly.

Do not introduce:

- generic repositories;
- Unit of Work wrappers around DbContext;
- generic CRUD service abstractions;
- persistence interfaces that merely duplicate DbSet operations.

Use async database APIs for I/O.

Propagate cancellation tokens where appropriate.

For read-only queries, use no-tracking behavior when appropriate.

# Query design

Prefer work that can execute efficiently in PostgreSQL.

Avoid:

- N+1 queries;
- database queries inside item-processing loops;
- loading complete tables when a filtered query is sufficient;
- premature `ToList` before filtering;
- repeated queries for data that can be fetched once;
- client-side evaluation when server-side translation is appropriate.

When processing many Xtream items, consider loading required data once and using dictionaries or sets for repeated lookups.

# PostgreSQL

PostgreSQL is the primary database.

Do not write persistence logic whose correctness depends only on EF Core InMemory behavior.

Consider PostgreSQL semantics for:

- comparisons;
- indexes;
- uniqueness;
- nullability;
- concurrency;
- generated values;
- query translation.

# Entity configuration

Keep EF-specific mapping in `XtreamForge.Database/Mappings`.

Prefer `IEntityTypeConfiguration<T>` following the existing project convention.

Do not put EF Core attributes into Domain solely for persistence convenience when fluent configuration is appropriate.

Define important database constraints explicitly.

# Migrations

Create a migration only when the schema changes.

Before creating one:

1. ensure the domain/model change is correct;
2. ensure entity mapping is correct;
3. build successfully;
4. generate the migration;
5. inspect the generated migration;
6. inspect the model snapshot change.

Do not blindly accept generated migrations.

Do not modify old applied migrations to represent a new schema change.

Do not manually edit the model snapshot without a specific justified reason.

# Validation

After persistence changes:

1. build the affected projects;
2. inspect EF/analyzer diagnostics;
3. run relevant tests;
4. inspect migration output if applicable;
5. ensure no secrets or connection strings were introduced.
