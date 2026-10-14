# Deploying XtreamForge on a Synology NAS (Container Manager)

## Overview

During development, Aspire starts everything. On the NAS, a **Docker Compose project** replaces Aspire and runs 3 containers. No port is published on the NAS: [Nginx Proxy Manager](https://nginxproxymanager.com/) (NPM), already running on the NAS, reaches the backend and the administration UI through its external Docker network `npm_internal`, and terminates TLS.

| Container | Role | Networks | Reached by |
|---|---|---|---|
| `xtreamforge-postgres` | database (migrations are applied automatically when the backend starts) | project network only | the backend |
| `xtreamforge-api` | Xtream proxy + admin API, port 8080 in the container | project network + `npm_internal` | NPM, and the Web UI |
| `xtreamforge-web` | Blazor administration UI, port 8080 in the container | `npm_internal` | NPM |

The images are built from the repository Dockerfiles (`src/XtreamForge.ApiService/Dockerfile` and `src/XtreamForge.Web/Dockerfile`). This folder holds the NAS `docker-compose.yml` and the template of its settings file, `.env.example`.

**Build the images on GitHub, not on the NAS.** Compiling .NET on a NAS is slow (and often fails for lack of memory). A GitHub Actions workflow builds the images on every push to `main` and publishes them to GitHub Container Registry (GHCR) for both Intel/AMD **and** ARM, so any Synology model works. The NAS only downloads them.

## Step 1: publish the images to GHCR (once)

1. The [publish-images.yml](../../.github/workflows/publish-images.yml) workflow runs on every push to `main` (or manually: **Actions** tab > "Publish images" > *Run workflow*).
2. Each image is published with two tags: `latest` (the newest version, used by the compose file) and the commit SHA (to go back to a specific version). They are named `ghcr.io/fred2u/xtreamforge-api` and `ghcr.io/fred2u/xtreamforge-web`.
3. The images are private by default (the repository is private). Two options:
   - **Simplest**: on GitHub, profile > **Packages** > each package > *Package settings* > *Change visibility* > **Public**. The code stays private; only the compiled images are visible.
   - **Keep them private**: create a *Personal access token (classic)* with only the `read:packages` scope, then on the NAS over SSH: `sudo docker login ghcr.io -u fred2u` (password = the token).

## Step 2: prepare the folder on the NAS

1. Nginx Proxy Manager must already run on the NAS, attached to an external Docker network named `npm_internal` (if it does not exist yet: `sudo docker network create npm_internal`, then add the NPM container to it). Note its subnet, needed below:

   ```bash
   sudo docker network inspect npm_internal --format '{{(index .IPAM.Config 0).Subnet}}'
   ```

2. **File Station** > `docker` shared folder (created by Container Manager) > create `xtreamforge`, and inside it a `postgres-data` subfolder.
3. Put both files in `/docker/xtreamforge`:
   - [docker-compose.yml](docker-compose.yml), unchanged;
   - [.env.example](.env.example), **renamed to `.env`** (right-click > Rename in File Station). It is the only file to edit, and it stays on the NAS: the TMDB token and the database password never go through GitHub.
4. In `.env`, set:
   - `TMDB_API_KEY`: on themoviedb.org > Settings > API, the **"API Read Access Token"** (the long one, not the short "API Key"). **Required**: without it no metadata is loaded and the catalogue returned to the devices stays empty;
   - `XTREAM_HOST_0`: the upstream provider host, for example `provider.example.com` (without `http://` or port). **Required**: without an allowed host, the backend fails at startup. `XTREAM_HOST_1` and `XTREAM_HOST_2` are optional, for up to two more providers (leave them empty otherwise); only the listed hosts can be proxied;
   - `REVERSE_PROXY_NETWORK`: the subnet of `npm_internal` noted above, for example `172.20.0.0/16`. **Required**: NPM is then trusted for its `X-Forwarded-Proto` and `X-Forwarded-Host` headers, so that the stream URLs given to the devices use your public scheme, host, and port;
   - `POSTGRES_PASSWORD`: a long password of your choice. Do not change it after the first start (the database keeps the original one).

To change the TMDB token or a provider host later: edit `.env`, then Container Manager > Project > `xtreamforge` > **Action** > **Build** (a plain stop/start does not re-read `.env`).

## Step 3: create the project in Container Manager

1. **Container Manager** > **Project** > **Create**.
2. Name: `xtreamforge`. Path: `/docker/xtreamforge`.
3. Source: **Use existing docker-compose.yml** (the one in the folder).
4. "Web portal settings" screen: leave everything unchecked.
5. **Done**. Container Manager pulls the images and starts the 3 containers. The first start takes about a minute (database creation).

## Step 4: create the proxy hosts in Nginx Proxy Manager

In NPM > **Hosts** > **Proxy Hosts** > **Add Proxy Host**, create two hosts (with a Let's Encrypt certificate on the **SSL** tab if you want HTTPS):

| Domain name (example) | Scheme | Forward hostname | Forward port | Options |
|---|---|---|---|---|
| `iptv.example.com` | `http` | `xtreamforge-api` | `8080` | none |
| `xtreamforge-admin.example.com` | `http` | `xtreamforge-web` | `8080` | **Websockets Support** required (Blazor Interactive Server), and an **Access List** to restrict who can open it |

On the `iptv` host, block the admin API for everyone: **Advanced** tab > Custom Nginx Configuration:

```nginx
location /api/admin/ { return 403; }
```

The Web UI is not affected: it calls the backend directly over `npm_internal`, not through NPM.

## Step 5: check

- Administration UI: `https://xtreamforge-admin.example.com`
- Backend health: `https://iptv.example.com/health` must return `Healthy`.
- On the IPTV devices, the Xtream server URL becomes the XtreamForge URL **followed by the provider address**, as `/{protocol}/{host}/{port}`: for a provider at `http://provider.example.com:8080`, use `https://iptv.example.com/http/provider.example.com/8080` (credentials unchanged: they are passed through to the provider). `provider.example.com` must be one of the `XTREAM_HOST_*` values.
- If something goes wrong: Container Manager > Container > `xtreamforge-api` > **Log**. Configuration errors (missing host, database connection) show up at startup.

## Updating

After a push to `main` and once the GitHub workflow has finished: Container Manager > Project > `xtreamforge` > **Action** > **Build** (or Stop, then Clean, then Start) to pull the `latest` images again. The database is kept in `/docker/xtreamforge/postgres-data`.

Upgrading from a previous version of this guide (PostgreSQL 17, data in `/docker/xtreamforge/postgres`): PostgreSQL 18 cannot open a PostgreSQL 17 data folder. Before switching, back up with `sudo docker exec xtreamforge-postgres pg_dump -U xtreamforge xtreamforge > backup.sql`; after the first start of the new project, stop `xtreamforge-api`, empty the new database with `sudo docker exec xtreamforge-postgres psql -U xtreamforge xtreamforge -c 'DROP SCHEMA public CASCADE; CREATE SCHEMA public;'`, restore with `sudo docker exec -i xtreamforge-postgres psql -U xtreamforge xtreamforge < backup.sql`, then start `xtreamforge-api` again.

## Security: read before exposing anything to the Internet

The admin API and the administration UI **are not authenticated**, and the admin API is served by the same container as the Xtream proxy. If the `iptv` proxy host is reachable from the Internet without the `/api/admin/` block, anyone can change your rules and sources. Until authentication exists:

- keep the `/api/admin/` block of step 4 on the `iptv` proxy host;
- protect the administration UI with an NPM **Access List** (local network only, or a login), or reach it through a **VPN** (Synology VPN Server, Tailscale, WireGuard);
- the Xtream proxy itself only forwards to the `XTREAM_HOST_*` hosts.

## Backup

Hyper Backup of the `/docker/xtreamforge/postgres-data` folder is enough, ideally with the project stopped. For a live backup: `sudo docker exec xtreamforge-postgres pg_dump -U xtreamforge xtreamforge > backup.sql`.
