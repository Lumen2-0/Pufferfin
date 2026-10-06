# Pufferfin: branches, upstream sync and release

This directory is Pufferfin-only and identical in `Pufferfin/Pufferfin` and
`Pufferfin/Pufferfin-web`. No Jellyfin workflow is modified: the Pufferfin
workflows are new files (`sync-upstream.yml`, `release.yml`).

## Branches

| Branch | Content | Rule |
|---|---|---|
| `upstream` | exact copy of the tracked Jellyfin branch | never edited by hand; fast-forward only, by `sync-upstream` |
| `main` | Pufferfin = `upstream` + Pufferfin code | default branch; `upstream` is merged into it, never rebased |
| `sync/<date>` | working branch of a sync | created by `sync-upstream`, deleted on merge |

**Tracked upstream branch: `release-12.z`** of `jellyfin/jellyfin` (server) and
`jellyfin/jellyfin-web` (client). This is the branch Jellyfin 12 releases come
from (`v12.0`, `v12.1`, `v12.2`, ...). Jellyfin's `master` branch is the
development branch of the next version (13.0.0) and is not tracked.

Switching the tracked branch (for example, moving to Jellyfin 13) means
changing `UPSTREAM_BRANCH` in `.github/workflows/sync-upstream.yml` in **both**
repositories, in the same window. It is a major version bump: human review, never
an automatic merge.

## `sync-upstream.yml`

Triggers: daily (04:23 UTC), a watch every two hours that does nothing until
Jellyfin publishes a new version, a `repository_dispatch` of type
`jellyfin-release`, and `workflow_dispatch`.

1. fetch the tracked Jellyfin branch and advance `upstream` (fast-forward only:
   if upstream rewrote its history, the workflow stops);
2. merge `upstream` into `sync/<date>`, starting from `main`;
3. build and run the tests;
4. no conflict and green tests: PR "Sync Jellyfin \<version\>". Auto-merge is
   only armed for a `x.y.Z` patch version (same `x.y`, higher `Z`: see the
   `classify` function in `sync-upstream.sh`) and when `PUFFERFIN_AUTOMERGE` is
   `true`;
5. conflict or failure: **draft** PR, with the list of conflicting files, a
   mention of `PUFFERFIN_SYNC_NOTIFY`, and a failed run (which triggers the
   GitHub Actions notification).

Only one sync PR is open at a time. Until it is merged, later runs **update**
it (merge upstream, then `main`, into its branch; never a force-push) instead of
opening a new one: a conflict resolution or an approval is never lost. If
upstream has not moved since, the run does nothing.

The open sync PR is found by its head branch (`sync/` prefix, same repository,
PRs from forks are ignored), never by its title.

On conflict, resolve on the PR branch:
`git switch sync/<date> && git merge origin/upstream && git merge origin/main`,
resolve, push.

Manual inputs: `dry_run` (pushes nothing, opens nothing) and `base_branch`
(target other than `main`, to simulate a conflict without touching `main`).

Sync PRs are merged with a **merge commit** only.

## `release.yml` and versioning

Image: `ghcr.io/pufferfin/pufferfin`, server + built web client, built by
`release.yml` in `Pufferfin/Pufferfin` on every merge to `main` (and on every
merge to the client's `main`, through `repository_dispatch`).

Version: **`<Jellyfin version>-pufferfin.<n>`**

- `<Jellyfin version>`: the version carried by the server's
  `SharedVersion.cs`, for example `12.2.0`;
- `<n>`: 1 for the first image of a Jellyfin version, then +1 for each
  release, computed from the existing git tags;
- example: `12.2.0-pufferfin.1`, `12.2.0-pufferfin.2`, then
  `12.3.0-pufferfin.1`.

Each published image gets the matching git tag on `Pufferfin/Pufferfin` and the
`latest` image tag.

**Guard**: server and client must track the same Jellyfin major version,
otherwise the release fails (`release-version.sh` on the server side, the
"Major-version guard" step on the client side).

The image starts from the official `jellyfin/jellyfin:<x.y>` image of the same
version (jellyfin-ffmpeg, system dependencies, `ENTRYPOINT`) and replaces its
server and client.

**Nothing is published until the `PUFFERFIN_PUBLISH` variable is `true`**: the
image is then a dry-run build.

## Repository configuration (owner decision)

| Item | Repository | Purpose |
|---|---|---|
| secret `PUFFERFIN_SYNC_TOKEN` | both | token (fine-grained PAT or GitHub App) with *Contents*, *Pull requests* and *Workflows* write access. Without it, `GITHUB_TOKEN` cannot push an upstream commit that touches `.github/workflows`, and its PRs do not trigger CI. |
| secret `PUFFERFIN_RELEASE_DISPATCH_TOKEN` | Pufferfin-web | token with *Contents* write access on `Pufferfin/Pufferfin`, to rebuild the image when the client changes |
| variable `PUFFERFIN_PUBLISH` | Pufferfin | `true` to publish the image and the tag |
| variable `PUFFERFIN_AUTOMERGE` | both | `true` to arm auto-merge for patch versions |
| variable `PUFFERFIN_SYNC_NOTIFY` | both | mentions added to failed sync PRs |
| *Allow auto-merge* | both | required by auto-merge |
| `main` protection | both | required approval + checks: this is what makes auto-merge wait |
| `upstream` protection | both | push restricted to the sync token, no force-push |

## Jellyfin workflows

They stay in the repository unchanged. Those that depend on Jellyfin
infrastructure are **disabled** (`gh workflow disable`), without editing or
deleting the file.
