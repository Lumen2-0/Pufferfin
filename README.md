<p align="center">
<picture>
  <source media="(prefers-color-scheme: dark)" srcset=".github/assets/pufferfin-logo-dark.svg">
  <source media="(prefers-color-scheme: light)" srcset=".github/assets/pufferfin-logo.svg">
  <img alt="Pufferfin logo" src=".github/assets/pufferfin-logo.svg" width="160">
</picture>
</p>

<h1 align="center">Pufferfin</h1>

<p align="center">
A complete media server, forked from <a href="https://github.com/jellyfin/jellyfin">Jellyfin</a>,<br/>
with an Apple TV style interface and built-in control of the *arr stack.
</p>

---

> [!WARNING]
> **Work in progress.** No release and no published Docker image yet. The catalog,
> requests, *arr configuration and admin pieces described below exist in this repository
> (`src/Pufferfin.Catalog`, `src/Pufferfin.Server`) but are not packaged for general use.

## What Pufferfin adds to Jellyfin

Pufferfin keeps everything Jellyfin does (library, playback, transcoding, accounts, resume,
subtitles) and adds:

- **An Apple TV style interface**: large artwork, black background, smooth animations,
  keyboard and remote navigation.
- **A complete catalog, not just your disks**: movies, shows and people from TMDB,
  audiobooks from Audible. Titles missing from your disks appear next to the ones you
  own.
- **A single "Play" / "Download" button**: if the title is there, it plays; otherwise,
  Pufferfin drives Radarr, Sonarr, Prowlarr, qBittorrent, Bazarr and Shelfmark to fetch
  it.

## A fork of Jellyfin

Pufferfin is a fork of [Jellyfin](https://github.com/jellyfin/jellyfin), the free software
media system. Most of the code in this repository is the work of Jellyfin and its
contributors, and Pufferfin would not exist without them. Pufferfin is neither affiliated
with nor endorsed by the Jellyfin team: for an issue specific to Pufferfin, open it here,
not with Jellyfin.

The fork tracks upstream closely: Jellyfin releases are merged into Pufferfin regularly,
and Pufferfin's own code is kept isolated so these merges stay simple.

## This repository

This repository contains the Pufferfin **server** (.NET), a fork of
[`jellyfin/jellyfin`](https://github.com/jellyfin/jellyfin). The web client lives in
[`Lumen2-0/Pufferfin-web`](https://github.com/Lumen2-0/Pufferfin-web); both ship
together, in the same Docker image.

## Typical deployment (Docker Compose)

> [!NOTE]
> The image is not published yet. The example below describes the intended deployment.

```yaml
services:
  pufferfin:
    # Tags follow the format <Jellyfin version>-pufferfin.<n>
    image: ghcr.io/pufferfin/pufferfin:<version>
    container_name: pufferfin
    restart: unless-stopped
    ports:
      - "8096:8096"
    volumes:
      - ./config:/config                 # configuration and database
      - ./cache:/cache                   # cache (images, transcoding)
      - /path/to/media:/media            # your media library
      - /path/to/downloads:/downloads    # download folder shared with the *arr apps
```

The interface is then available at `http://<host>:8096`.

### Configuration

**All configuration is done from the admin panel**: *arr services, qBittorrent, catalogs,
languages, quality rules. Each service has a connection test there.

Environment variables are only used to **seed** the configuration on first start and for
automated deployments. A value provided by the environment shows up in the panel as
read-only. Secrets (API keys, credentials) stay on the server and are never sent back to
the interface.

## Development

The server builds like Jellyfin, with the .NET SDK specified in
[`global.json`](global.json):

```bash
dotnet build
dotnet test
dotnet run --project Jellyfin.Server --webdir /absolute/path/to/Pufferfin-web/dist
```

To build the web client, see [`Lumen2-0/Pufferfin-web`](https://github.com/Lumen2-0/Pufferfin-web).

## License

Like Jellyfin, Pufferfin is distributed under the **GNU General Public License v2.0**. See
[`LICENSE`](LICENSE). Code originating from Jellyfin remains the property of its authors;
changes made by Pufferfin are released under the same license.
