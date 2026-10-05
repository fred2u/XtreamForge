# Deploying XtreamForge on a Synology NAS (Container Manager)

## Overview

During development, Aspire starts everything. On the NAS, a **Docker Compose project** replaces Aspire and runs 3 containers:

| Container | Role | Port on the NAS |
|---|---|---|
| `postgres` | database (migrations are applied automatically when the backend starts) | none (internal) |
| `api` | Xtream proxy + admin API | `API_PORT` (default 8080) |
| `web` | Blazor administration UI | `ADMIN_PORT` (default 8081) |

The images are built from the repository Dockerfiles (`src/XtreamForge.ApiService/Dockerfile` and `src/XtreamForge.Web/Dockerfile`). This folder holds the NAS `docker-compose.yml` and the template of its settings file, `.env.example`.

**Build the images on GitHub, not on the NAS.** Compiling .NET on a NAS is slow (and often fails for lack of memory). A GitHub Actions workflow builds the images on every push to `main` and publishes them to GitHub Container Registry (GHCR) for both Intel/AMD **and** ARM, so any Synology model works. The NAS only downloads them.

## Step 1: publish the images to GHCR (once)

1. The [publish-images.yml](../../.github/workflows/publish-images.yml) workflow runs on every push to `main` (or manually: **Actions** tab > "Publish images" > *Run workflow*).
2. Each image is published with two tags: `latest` (the newest version, used by the compose file) and the commit SHA (to go back to a specific version). They are named `ghcr.io/fred2u/xtreamforge-api` and `ghcr.io/fred2u/xtreamforge-web`.
3. The images are private by default (the repository is private). Two options:
   - **Simplest**: on GitHub, profile > **Packages** > each package > *Package settings* > *Change visibility* > **Public**. The code stays private; only the compiled images are visible.
   - **Keep them private**: create a *Personal access token (classic)* with only the `read:packages` scope, then on the NAS over SSH: `sudo docker login ghcr.io -u fred2u` (password = the token).

## Step 2: prepare the folder on the NAS

1. **File Station** > `docker` shared folder (created by Container Manager) > create `xtreamforge`, and inside it a `postgres` subfolder.
2. Put both files in `/docker/xtreamforge`:
   - [docker-compose.yml](docker-compose.yml), unchanged;
   - [.env.example](.env.example), **renamed to `.env`** (right-click > Rename in File Station). It is the only file to edit, and it stays on the NAS: the TMDB token and the database password never go through GitHub.
3. In `.env`, set:
   - `API_PORT`: host port of the Xtream proxy (default 8080);
   - `ADMIN_PORT`: host port of the administration UI (default 8081);
   - `TMDB_API_KEY`: on themoviedb.org > Settings > API, the **"API Read Access Token"** (the long one, not the short "API Key"). **Required**: without it no metadata is loaded and the catalogue returned to the devices stays empty;
   - `XTREAM_HOST`: the upstream provider host, for example `provider.example.com` (without `http://` or port). **Required**: without an allowed host, the backend fails at startup;
   - `POSTGRES_PASSWORD`: a long password of your choice. Do not change it after the first start (the database keeps the original one).

To change a port or the TMDB token later: edit `.env`, then Container Manager > Project > `xtreamforge` > **Action** > **Build** (a plain stop/start does not re-read `.env`).

## Step 3: create the project in Container Manager

1. **Container Manager** > **Project** > **Create**.
2. Name: `xtreamforge`. Path: `/docker/xtreamforge`.
3. Source: **Use existing docker-compose.yml** (the one in the folder).
4. "Web portal settings" screen: leave everything unchecked.
5. **Done**. Container Manager pulls the images and starts the 3 containers. The first start takes about a minute (database creation).

## Step 4: check

- Administration UI: `http://NAS-IP:8081` (or your `ADMIN_PORT`)
- Backend health: `http://NAS-IP:8080/health` (or your `API_PORT`) must return `Healthy`.
- On the IPTV devices, the Xtream server URL becomes `http://NAS-IP:8080` or your `API_PORT` (credentials unchanged: they are passed through to the provider).
- If something goes wrong: Container Manager > Container > `xtreamforge-api` > **Log**. Configuration errors (missing host, database connection) show up at startup.

## Updating

After a push to `main` and once the GitHub workflow has finished: Container Manager > Project > `xtreamforge` > **Action** > **Build** (or Stop, then Clean, then Start) to pull the `latest` images again. The database is kept in `/docker/xtreamforge/postgres`.

## Security: read before exposing anything to the Internet

The admin API **is not authenticated** and shares port 8080 with the Xtream proxy. If you forward port 8080 on your router to watch away from home, anyone can change your rules and sources. Until authentication exists:

- keep everything on the **local network**, or reach it from outside through a **VPN** (Synology VPN Server, Tailscale, WireGuard);
- **never** expose port 8081 (the administration UI).

If you later put the DSM reverse proxy (Login Portal) in front, declare its network in `ReverseProxy__KnownNetworks__0` (the project's Docker network, visible in Container Manager > Network, for example `172.18.0.0/16`) so that the stream URLs are correct.

## Backup

Hyper Backup of the `/docker/xtreamforge/postgres` folder is enough, ideally with the project stopped. For a live backup: `sudo docker exec xtreamforge-postgres pg_dump -U xtreamforge xtreamforge > backup.sql`.
