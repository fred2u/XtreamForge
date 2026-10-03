# Technical debt

Known bugs, design issues, and improvement opportunities identified in the current implementation.

Remove an entry in the same change that fixes it. Keep the remaining entries accurate when the related code changes.

## Design

### Catalogue can stay empty

Items are returned only when a TMDB ID is known and its TMDB metadata (`TmdbInfo`) is loaded. Without `Tmdb:ApiKey`, no metadata is ever loaded, so no item is ever returned, even when the provider exposes `tmdb_id`; the first catalogue requests are mostly empty until the background lookups and loads catch up.

Fix: decide on a degraded mode (for example an option to return items without a TMDB ID or without TMDB metadata).


### Migrations applied at startup

`UseDatabase` runs `Database.Migrate()` at startup; several API instances would migrate concurrently.

Fix: acceptable for a single instance; move migrations to a dedicated step if the API is scaled out.

### Watch history only records movies

Series episodes are not recorded: the episode stream URL (`series/{username}/{password}/{episodeId}.{ext}`) only carries the provider episode ID, while TMDB mappings are keyed by series ID, and no Xtream action resolves an episode to its series. A playback is also recorded from its first request, whatever the part actually watched.

Fix: map episodes to their series (for example from the `get_series_info` payloads seen by `ItemGetEndpoint`), and, if needed, estimate the watched part from the byte ranges served for a playback.
