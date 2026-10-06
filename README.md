<p align="center">
<picture>
  <source media="(prefers-color-scheme: dark)" srcset=".github/assets/pufferfin-logo-dark.svg">
  <source media="(prefers-color-scheme: light)" srcset=".github/assets/pufferfin-logo.svg">
  <img alt="Logo Pufferfin" src=".github/assets/pufferfin-logo.svg" width="160">
</picture>
</p>

<h1 align="center">Pufferfin</h1>

<p align="center">
Un serveur multimédia complet, fork de <a href="https://github.com/jellyfin/jellyfin">Jellyfin</a>,<br/>
avec une interface façon Apple TV et le pilotage de la stack *arr.
</p>

---

> [!WARNING]
> **Projet en construction.** Rien n'est encore livré : pas de version publiée, pas d'image
> Docker disponible. Ce dépôt est aujourd'hui très proche de Jellyfin ; les fonctionnalités
> décrites ci-dessous sont la cible du projet, pas son état actuel.

## Ce que Pufferfin ajoute à Jellyfin

Pufferfin garde tout ce que fait Jellyfin — bibliothèque, lecture, transcodage, comptes,
reprise de lecture, sous-titres — et y ajoute :

- **Une interface façon Apple TV** : grands visuels, fond noir, animations douces, navigation
  au clavier et à la télécommande.
- **Un catalogue complet, pas seulement le disque** : films, séries et personnes depuis TMDB,
  livres audio depuis Audible. Les titres absents de vos disques apparaissent à côté de ceux
  que vous possédez.
- **Un bouton unique « Lire » / « Télécharger »** : si le titre est là, on le lit ; sinon,
  Pufferfin pilote Radarr, Sonarr, Prowlarr, qBittorrent, Bazarr et Shelfmark pour aller le
  chercher.

## Un fork de Jellyfin

Pufferfin est un fork de [Jellyfin](https://github.com/jellyfin/jellyfin), le système
multimédia libre. L'essentiel du code de ce dépôt est celui de Jellyfin et de ses
contributeurs, et Pufferfin n'existerait pas sans leur travail. Pufferfin n'est ni affilié
à l'équipe Jellyfin ni soutenu par elle : pour un problème propre à Pufferfin, ouvrez une
issue ici, pas chez Jellyfin.

Le fork suit l'amont de près : les versions de Jellyfin sont fusionnées régulièrement dans
Pufferfin, et le code propre à Pufferfin est isolé pour que ces fusions restent simples.

## Ce dépôt

Ce dépôt contient le **serveur** Pufferfin (.NET), fork de
[`jellyfin/jellyfin`](https://github.com/jellyfin/jellyfin). Le client web se trouve dans
[`Pufferfin/Pufferfin-web`](https://github.com/Pufferfin/Pufferfin-web) ; les deux sont
livrés ensemble, dans la même image Docker.

## Déploiement type (Docker Compose)

> [!NOTE]
> L'image n'est pas encore publiée. L'exemple ci-dessous décrit le déploiement visé.

```yaml
services:
  pufferfin:
    # Tags au format <version Jellyfin>-pufferfin.<n>
    image: ghcr.io/pufferfin/pufferfin:<version>
    container_name: pufferfin
    restart: unless-stopped
    ports:
      - "8096:8096"
    volumes:
      - ./config:/config                 # configuration et base de données
      - ./cache:/cache                   # cache (images, transcodage)
      - /chemin/vers/media:/media        # votre médiathèque
      - /chemin/vers/downloads:/downloads # dossier de téléchargement partagé avec les *arr
```

L'interface est ensuite accessible sur `http://<hôte>:8096`.

### Configuration

**Toute la configuration s'effectue depuis le panel d'administration** : services *arr,
qBittorrent, catalogues, langues, règles de qualité. Chaque service y dispose d'un test de
connexion.

Les variables d'environnement servent uniquement à **amorcer** la configuration au premier
démarrage et aux déploiements automatisés. Une valeur fournie par l'environnement apparaît
dans le panel en lecture seule. Les secrets (clés d'API, identifiants) restent sur le
serveur et ne sont jamais renvoyés à l'interface.

## Développement

Le serveur se compile comme Jellyfin, avec le SDK .NET indiqué dans
[`global.json`](global.json) :

```bash
dotnet build
dotnet test
dotnet run --project Jellyfin.Server --webdir /chemin/absolu/vers/Pufferfin-web/dist
```

Pour compiler le client web, voir [`Pufferfin/Pufferfin-web`](https://github.com/Pufferfin/Pufferfin-web).

## Licence

Pufferfin est distribué, comme Jellyfin, sous **GNU General Public License v2.0**. Voir
[`LICENSE`](LICENSE). Le code issu de Jellyfin reste la propriété de ses auteurs ; les
modifications apportées par Pufferfin sont publiées sous la même licence.
