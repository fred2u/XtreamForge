# Déployer XtreamForge sur un Synology (Container Manager)

## Le principe

Sur ton PC, Aspire lance tout pour toi. Sur le NAS, on remplace Aspire par un **projet Docker Compose** avec 3 conteneurs :

| Conteneur | Rôle | Port sur le NAS |
|---|---|---|
| `postgres` | base de données (les migrations sont appliquées automatiquement au démarrage de l'API) | aucun (interne) |
| `api` | proxy Xtream + API d'admin | `API_PORT` (8080 par défaut) |
| `web` | interface d'administration Blazor | `ADMIN_PORT` (8081 par défaut) |

Les images sont construites à partir des Dockerfiles du repo (`src/XtreamForge.ApiService/Dockerfile` et `src/XtreamForge.Web/Dockerfile`). Ce dossier contient le `docker-compose.yml` du NAS et le modèle de son fichier de réglages, `.env.example`.

**Recommandation : construire les images sur GitHub, pas sur le NAS.** Compiler du .NET sur un NAS est lent (et souvent impossible faute de RAM). Un workflow GitHub Actions construit les images à chaque push sur `main` et les publie sur GitHub Container Registry (GHCR), en version Intel/AMD **et** ARM, donc ça marche quel que soit ton modèle de Synology. Le NAS n'a plus qu'à les télécharger.

## Étape 1 : publier les images sur GHCR (une seule fois)

1. Le workflow [publish-images.yml](../../.github/workflows/publish-images.yml) tourne à chaque push sur `main` (ou à la main : onglet **Actions** > « Publish images » > *Run workflow*).
2. Les images sont publiées avec deux tags : `latest` (la dernière version, utilisée par le compose) et le hash du commit (pour revenir à une version précise). Elles s'appellent `ghcr.io/fred2u/xtreamforge-api` et `ghcr.io/fred2u/xtreamforge-web`.
3. Les images sont privées par défaut (le repo est privé). Deux options :
   - **Le plus simple** : sur GitHub, profil > **Packages** > chaque package > *Package settings* > *Change visibility* > **Public**. Le code reste privé, seules les images compilées sont visibles.
   - **Garder privé** : créer un *Personal access token (classic)* avec uniquement le droit `read:packages`, puis sur le NAS en SSH : `sudo docker login ghcr.io -u fred2u` (mot de passe = le token).

## Étape 2 : préparer le dossier sur le NAS

1. **File Station** > dossier partagé `docker` (créé par Container Manager) > créer `xtreamforge`, puis dedans un sous-dossier `postgres`.
2. Déposer dans `/docker/xtreamforge` les deux fichiers fournis :
   - [docker-compose.yml](docker-compose.yml), à ne pas modifier ;
   - [.env.example](.env.example), **renommé en `.env`** (clic droit > Renommer dans File Station). C'est le seul fichier à éditer, et il ne reste que sur le NAS : ta clé TMDB et ton mot de passe ne passent jamais par GitHub.
3. Dans `.env`, régler :
   - `API_PORT` : port du proxy Xtream sur le NAS (8080 par défaut) ;
   - `ADMIN_PORT` : port de l'interface d'admin (8081 par défaut) ;
   - `TMDB_API_KEY` : sur themoviedb.org > Paramètres > API, le **« jeton d'accès en lecture à l'API »** (le long, pas la « clé API » courte). **Obligatoire** : sans lui aucune métadonnée n'est chargée et le catalogue renvoyé aux appareils reste vide ;
   - `XTREAM_HOST` : l'hôte de ton fournisseur, par exemple `monfournisseur.com` (sans `http://` ni port). **Obligatoire** : sans hôte autorisé, l'API refuse de démarrer ;
   - `POSTGRES_PASSWORD` : un mot de passe long que tu inventes. Ne le change plus après le premier démarrage (la base garde l'ancien).

Pour changer un port ou la clé TMDB plus tard : modifier `.env`, puis Container Manager > Projet > `xtreamforge` > **Action** > **Construire** (un simple arrêt/redémarrage ne relit pas `.env`).

## Étape 3 : créer le projet dans Container Manager

1. **Container Manager** > **Projet** > **Créer**.
2. Nom : `xtreamforge`. Chemin : `/docker/xtreamforge`.
3. Source : **Utiliser le docker-compose.yml existant** (celui déposé dans le dossier).
4. Écran « Paramètres du portail web » : ne rien cocher (inutile).
5. **Terminé**. Container Manager télécharge les images et démarre les 3 conteneurs. Le premier démarrage prend une minute (création de la base).

## Étape 4 : vérifier

- Interface d'admin : `http://IP-DU-NAS:8081` (ou ton `ADMIN_PORT`)
- Santé de l'API : `http://IP-DU-NAS:8080/health` (ou ton `API_PORT`) doit afficher `Healthy`.
- Sur tes appareils IPTV, l'URL du serveur Xtream devient `http://IP-DU-NAS:8080` ou ton `API_PORT` (identifiants inchangés : ils sont transmis tels quels au fournisseur).
- En cas de souci : Container Manager > Conteneur > `xtreamforge-api` > **Journal**. Les erreurs de config (hôte manquant, connexion base) apparaissent dès le démarrage.

## Mettre à jour

Après un push sur `main` et la fin du workflow GitHub : Container Manager > Projet > `xtreamforge` > **Action** > **Construire** (ou Arrêter puis « Nettoyer » puis Démarrer) pour retélécharger les images `latest`. La base est conservée dans `/docker/xtreamforge/postgres`.

## Sécurité : à lire avant d'ouvrir vers Internet

L'API d'administration **n'a pas d'authentification** et partage le port 8080 avec le proxy Xtream. Si tu rediriges le port 8080 sur ta box pour regarder hors de chez toi, n'importe qui pourra modifier tes règles et sources. Tant que l'auth n'existe pas :

- garde tout en **réseau local**, ou accède depuis l'extérieur via un **VPN** (VPN Server de Synology, Tailscale, WireGuard) ;
- n'expose **jamais** le port 8081 (l'interface d'admin).

Si plus tard tu passes par le reverse proxy de DSM (Portail de connexion), il faudra déclarer son IP dans `ReverseProxy__KnownNetworks__0` (le réseau Docker du projet, visible dans Container Manager > Réseau, par exemple `172.18.0.0/16`) pour que les URLs de flux soient correctes.

## Sauvegarde

Hyper Backup sur le dossier `/docker/xtreamforge/postgres` suffit, idéalement avec le projet arrêté. Pour une sauvegarde à chaud : `sudo docker exec xtreamforge-postgres pg_dump -U xtreamforge xtreamforge > sauvegarde.sql`.
