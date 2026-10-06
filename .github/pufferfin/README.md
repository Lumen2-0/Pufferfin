# Pufferfin — branches, synchronisation amont et release

Ce dossier est propre à Pufferfin et identique dans `Pufferfin/Pufferfin` et
`Pufferfin/Pufferfin-web`. Aucun workflow Jellyfin n'est modifié : les
workflows Pufferfin sont des fichiers nouveaux (`sync-upstream.yml`,
`release.yml`).

## Branches

| Branche | Contenu | Règle |
|---|---|---|
| `upstream` | copie exacte de la branche Jellyfin suivie | jamais modifiée à la main ; avance rapide uniquement, par `sync-upstream` |
| `main` | Pufferfin = `upstream` + le code Pufferfin | branche par défaut ; on y fusionne `upstream`, on ne rebase jamais |
| `sync/<date>` | branche de travail d'une synchronisation | créée par `sync-upstream`, supprimée à la fusion |

**Branche amont suivie : `release-12.z`** de `jellyfin/jellyfin` (serveur) et
de `jellyfin/jellyfin-web` (client). C'est la branche d'où sortent les versions
publiées de Jellyfin 12 (`v12.0`, `v12.1`, `v12.2`…). La branche `master` de
Jellyfin est la branche de développement de la version suivante (13.0.0) et
n'est pas suivie.

Changer de branche suivie (ex. passage à Jellyfin 13) se fait en modifiant
`UPSTREAM_BRANCH` dans `.github/workflows/sync-upstream.yml` des **deux**
dépôts, dans la même fenêtre. C'est une montée de version majeure : relecture
humaine, jamais de fusion automatique.

## `sync-upstream.yml`

Déclencheurs : chaque jour (04:23 UTC), une veille toutes les deux heures qui
ne fait rien tant que Jellyfin n'a pas publié de nouvelle version, un
`repository_dispatch` de type `jellyfin-release`, et `workflow_dispatch`.

1. récupère la branche Jellyfin suivie et avance `upstream` (avance rapide
   seulement : si l'amont a réécrit son historique, le workflow s'arrête) ;
2. fusionne `upstream` dans `sync/<date>` partant de `main` ;
3. compile et lance les tests ;
4. sans conflit et tests verts : PR « Sync Jellyfin \<version\> ». L'auto-merge
   n'est armé que pour une version corrective `x.y.Z` (même `x.y`, `Z`
   croissant — fonction `classify` de `sync-upstream.sh`) et si
   `PUFFERFIN_AUTOMERGE` vaut `true` ;
5. conflit ou échec : PR en **brouillon**, avec la liste des fichiers en
   conflit, mention de `PUFFERFIN_SYNC_NOTIFY`, et exécution en échec (ce qui
   déclenche la notification GitHub Actions).

En cas de conflit, la branche `sync/<date>` pointe sur l'amont. Résolution :
`git switch sync/<date> && git merge origin/main`, résoudre, pousser.

Entrées manuelles : `dry_run` (ne pousse rien, n'ouvre rien) et `base_branch`
(cible autre que `main`, pour simuler un conflit sans toucher `main`).

Les PR de sync se fusionnent en **merge commit** uniquement.

## `release.yml` et versionnage

Image : `ghcr.io/pufferfin/pufferfin`, serveur + client web compilé, construite
par `release.yml` de `Pufferfin/Pufferfin` à chaque fusion sur `main` (et à
chaque fusion sur `main` du client, via `repository_dispatch`).

Version : **`<version Jellyfin>-pufferfin.<n>`**

- `<version Jellyfin>` : la version portée par `SharedVersion.cs` du serveur,
  ex. `12.2.0` ;
- `<n>` : 1 pour la première image d'une version Jellyfin, puis +1 à chaque
  release, calculé depuis les tags git existants ;
- exemple : `12.2.0-pufferfin.1`, `12.2.0-pufferfin.2`, puis
  `12.3.0-pufferfin.1`.

Chaque image publiée reçoit le tag git correspondant sur `Pufferfin/Pufferfin`
et le tag d'image `latest`.

**Garde-fou** : serveur et client doivent suivre la même version majeure de
Jellyfin, sinon la release échoue (`release-version.sh` côté serveur, étape
« Major-version guard » côté client).

L'image part de l'image officielle `jellyfin/jellyfin:<x.y>` de la même
version (jellyfin-ffmpeg, dépendances système, `ENTRYPOINT`) et en remplace le
serveur et le client.

**Rien n'est publié tant que la variable `PUFFERFIN_PUBLISH` ne vaut pas
`true`** : l'image est alors construite à blanc.

## Configuration du dépôt (décision du propriétaire)

| Élément | Dépôt | Rôle |
|---|---|---|
| secret `PUFFERFIN_SYNC_TOKEN` | les deux | jeton (PAT fine-grained ou GitHub App) avec *Contents*, *Pull requests* et *Workflows* en écriture. Sans lui, `GITHUB_TOKEN` ne peut pas pousser un commit amont qui touche `.github/workflows`, et ses PR ne déclenchent pas la CI. |
| secret `PUFFERFIN_RELEASE_DISPATCH_TOKEN` | Pufferfin-web | jeton avec *Contents* en écriture sur `Pufferfin/Pufferfin`, pour relancer l'image quand le client change |
| variable `PUFFERFIN_PUBLISH` | Pufferfin | `true` pour publier l'image et le tag |
| variable `PUFFERFIN_AUTOMERGE` | les deux | `true` pour armer l'auto-merge des versions correctives |
| variable `PUFFERFIN_SYNC_NOTIFY` | les deux | mentions ajoutées aux PR de sync en échec |
| *Allow auto-merge* | les deux | requis par l'auto-merge |
| protection de `main` | les deux | approbation requise + checks : c'est elle qui fait attendre l'auto-merge |
| protection de `upstream` | les deux | push réservé au jeton de sync, pas de force-push |

## Workflows Jellyfin

Ils restent dans le dépôt à l'identique. Ceux qui dépendent de
l'infrastructure Jellyfin se **désactivent** (`gh workflow disable`), sans
modifier ni supprimer le fichier.
