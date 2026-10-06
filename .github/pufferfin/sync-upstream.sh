#!/usr/bin/env bash
# PUFFERFIN: synchronization with upstream Jellyfin. Pufferfin-only file,
# identical in Pufferfin/Pufferfin and Pufferfin/Pufferfin-web.
# Contract: .github/pufferfin/README.md
#
#   sync-upstream.sh prepare   update `upstream`, merge it into sync/<date>
#   sync-upstream.sh publish   push sync/<date> and open or update the PR
#                              (draft on conflict or red tests)
#
# Environment:
#   UPSTREAM_REPO     tracked Jellyfin repository (jellyfin/jellyfin, jellyfin/jellyfin-web)
#   UPSTREAM_BRANCH   tracked release branch (release-12.z)
#   UPSTREAM_URL      upstream git URL (defaults to https://github.com/$UPSTREAM_REPO.git)
#   BASE_BRANCH       target Pufferfin branch (main)
#   COMPANION_REPO    the other Pufferfin repository, for the major-version guard
#   DRY_RUN           true: push nothing, open no PR
#   WATCH_ONLY        true: only sync when Jellyfin has published a new version
#   AUTOMERGE         true: arm auto-merge for x.y.Z patch versions
#   NOTIFY            mentions added to failed sync PRs (e.g. "@Pufferfin/maintainers")
#   TEST_OUTCOME      (publish) result of the build + test step: success | failure
#   STATE_FILE        state file shared between prepare and publish
#
# An open sync PR is found by its head branch name (`sync/` prefix, same
# repository), never by its title: PR titles and bodies can change freely.
set -euo pipefail

: "${UPSTREAM_REPO:?}" "${UPSTREAM_BRANCH:?}"
UPSTREAM_URL="${UPSTREAM_URL:-https://github.com/${UPSTREAM_REPO}.git}"
BASE_BRANCH="${BASE_BRANCH:-main}"
DRY_RUN="${DRY_RUN:-false}"
WATCH_ONLY="${WATCH_ONLY:-false}"
AUTOMERGE="${AUTOMERGE:-false}"
NOTIFY="${NOTIFY:-}"
STATE_FILE="${STATE_FILE:-${RUNNER_TEMP:-/tmp}/pufferfin-sync.env}"
JF_REF="refs/remotes/jellyfin/${UPSTREAM_BRANCH}"
RUN_URL="${GITHUB_SERVER_URL:-https://github.com}/${GITHUB_REPOSITORY:-local}/actions/runs/${GITHUB_RUN_ID:-0}"

log() { printf '%s\n' "$*" >&2; }

output() {
    # GitHub Actions step output, plus state for the publish step.
    printf '%s=%s\n' "$1" "$2" >> "${GITHUB_OUTPUT:-/dev/null}"
    printf '%s=%q\n' "$1" "$2" >> "$STATE_FILE"
}

summary() {
    printf '%s\n' "$*" >> "${GITHUB_STEP_SUMMARY:-/dev/null}"
}

# Jellyfin version carried by a tree: SharedVersion.cs (server) or package.json (web).
jellyfin_version() {
    local ref="$1"
    if git cat-file -e "${ref}:SharedVersion.cs" 2>/dev/null; then
        git show "${ref}:SharedVersion.cs" | sed -n 's/.*AssemblyVersion("\([^"]*\)").*/\1/p' | head -n 1
    else
        git show "${ref}:package.json" | sed -n 's/^  "version": "\([^"]*\)".*/\1/p' | head -n 1
    fi
}

# Kind of version change: patch | minor | major | none | unknown.
# Only `patch` (x.y.Z, same x.y, higher Z) allows automatic merging.
classify() {
    local old="$1" new="$2" re='^([0-9]+)\.([0-9]+)\.([0-9]+)$'
    [[ "$old" =~ $re ]] || { echo unknown; return; }
    local o1="${BASH_REMATCH[1]}" o2="${BASH_REMATCH[2]}" o3="${BASH_REMATCH[3]}"
    [[ "$new" =~ $re ]] || { echo unknown; return; }
    local n1="${BASH_REMATCH[1]}" n2="${BASH_REMATCH[2]}" n3="${BASH_REMATCH[3]}"
    if [ "$old" = "$new" ]; then echo none
    elif [ "$n1" -ne "$o1" ]; then echo major
    elif [ "$n2" -ne "$o2" ]; then echo minor
    elif [ "$n3" -gt "$o3" ]; then echo patch
    else echo unknown
    fi
}

# Version of the other Pufferfin repository, read from its main branch.
companion_version() {
    [ -n "${COMPANION_REPO:-}" ] || return 0
    local file
    case "$COMPANION_REPO" in
        *-web) file=package.json ;;
        *) file=SharedVersion.cs ;;
    esac
    curl -fsSL --max-time 20 --retry 2 "https://raw.githubusercontent.com/${COMPANION_REPO}/${BASE_BRANCH}/${file}" 2>/dev/null \
        | sed -n -e 's/.*AssemblyVersion("\([^"]*\)").*/\1/p' -e 's/^  "version": "\([^"]*\)".*/\1/p' \
        | head -n 1 || true
}

# Open sync PR targeting BASE_BRANCH: "<number> <branch>", the oldest one.
# PRs from forks are ignored: on a public repository, anyone could otherwise
# block every sync with a fork branch named sync/*.
open_sync_pr() {
    gh pr list --base "$BASE_BRANCH" --state open --json number,headRefName,isCrossRepository \
        --jq '[.[] | select(.isCrossRepository == false) | select(.headRefName | startswith("sync/"))] | sort_by(.number) | first | select(. != null) | "\(.number) \(.headRefName)"'
}

push() {
    if [ "$DRY_RUN" = true ]; then
        log "[dry run] git push origin $*"
    else
        git push origin "$@"
    fi
}

prepare() {
    : > "$STATE_FILE"
    git config user.name "pufferfin-sync[bot]"
    git config user.email "41898282+github-actions[bot]@users.noreply.github.com"

    git fetch --no-tags origin "+refs/heads/${BASE_BRANCH}:refs/remotes/origin/${BASE_BRANCH}"
    if ! git fetch --no-tags origin "+refs/heads/upstream:refs/remotes/origin/upstream"; then
        log "::error::The 'upstream' branch does not exist: it must be bootstrapped by hand (see .github/pufferfin/README.md)."
        exit 1
    fi
    git fetch --no-tags "$UPSTREAM_URL" "+refs/heads/${UPSTREAM_BRANCH}:${JF_REF}"

    local jf_sha up_sha
    jf_sha="$(git rev-parse "$JF_REF")"
    up_sha="$(git rev-parse refs/remotes/origin/upstream)"

    # 1. `upstream` is an exact copy of the upstream branch, fast-forward only.
    if ! git merge-base --is-ancestor "$up_sha" "$jf_sha"; then
        log "::error::${UPSTREAM_REPO}@${UPSTREAM_BRANCH} does not descend from 'upstream' (upstream history rewritten?). Nothing is forced: human intervention required."
        exit 1
    fi

    local old new up_version kind
    old="$(jellyfin_version "refs/remotes/origin/${BASE_BRANCH}")"
    new="$(jellyfin_version "$JF_REF")"
    up_version="$(jellyfin_version refs/remotes/origin/upstream)"

    if [ "$WATCH_ONLY" = true ] && [ "$new" = "$up_version" ]; then
        log "Watch: no new Jellyfin version (${new}), nothing to do."
        output status idle
        summary "Watch: no new Jellyfin version (\`${new}\`)."
        return
    fi

    if [ "$jf_sha" != "$up_sha" ]; then
        log "upstream: ${up_sha:0:10} -> ${jf_sha:0:10} (${UPSTREAM_REPO}@${UPSTREAM_BRANCH})"
        push "${jf_sha}:refs/heads/upstream"
    fi

    # 2. Anything to merge?
    if git merge-base --is-ancestor "$jf_sha" "refs/remotes/origin/${BASE_BRANCH}"; then
        log "${BASE_BRANCH} already contains ${UPSTREAM_REPO}@${jf_sha:0:10} (${new})."
        output status up-to-date
        output version "$new"
        summary "\`${BASE_BRANCH}\` is up to date with Jellyfin \`${new}\` (\`${jf_sha:0:10}\`)."
        return
    fi

    kind="$(classify "$old" "$new")"
    local branch pr_number="" status conflicts=""

    # An open sync PR is updated, never replaced: it may carry a conflict
    # resolution or an approval.
    local existing
    existing="$(open_sync_pr)"
    if [ -n "$existing" ]; then
        read -r pr_number branch <<< "$existing"
        git fetch --no-tags origin "+refs/heads/${branch}:refs/remotes/origin/${branch}"
        if git merge-base --is-ancestor "$jf_sha" "refs/remotes/origin/${branch}"; then
            log "PR #${pr_number} (${branch}) already contains ${UPSTREAM_REPO}@${jf_sha:0:10}: nothing to do."
            output status pending
            summary "Sync PR #${pr_number} (\`${branch}\`) already covers Jellyfin \`${new}\`."
            return
        fi

        # 3a. Upstream moved again: merge it into the existing branch, then
        # main if needed. A conflict leaves the branch untouched.
        git switch -q -C "$branch" "refs/remotes/origin/${branch}"
        if ! git merge --no-edit \
            -m "Merge Jellyfin ${new} (${UPSTREAM_REPO}@${UPSTREAM_BRANCH}) into ${branch}" \
            "$jf_sha"; then
            status=conflict
            conflicts="$(git diff --name-only --diff-filter=U | tr '\n' ' ')"
            git merge --abort
        elif git merge-base --is-ancestor "refs/remotes/origin/${BASE_BRANCH}" HEAD \
            || git merge --no-edit -m "Merge ${BASE_BRANCH} into ${branch}" "refs/remotes/origin/${BASE_BRANCH}"; then
            status=merged
        else
            status=conflict
            conflicts="$(git diff --name-only --diff-filter=U | tr '\n' ' ')"
            git merge --abort
        fi
    else
        branch="sync/$(date -u +%F)"
        if git ls-remote --exit-code --heads origin "$branch" >/dev/null 2>&1; then
            branch="${branch}-${GITHUB_RUN_ID:-$(date -u +%H%M%S)}"
        fi

        # 3b. Merge (never rebase) upstream into sync/<date>, starting from main.
        git switch -q -C "$branch" "refs/remotes/origin/${BASE_BRANCH}"
        if git merge --no-ff --no-edit \
            -m "Merge Jellyfin ${new} (${UPSTREAM_REPO}@${UPSTREAM_BRANCH}) into ${BASE_BRANCH}" \
            "$jf_sha"; then
            status=merged
        else
            status=conflict
            conflicts="$(git diff --name-only --diff-filter=U | tr '\n' ' ')"
            git merge --abort
            # The branch points at upstream: the PR shows the conflict, and it
            # is resolved by merging main into this branch.
            git switch -q -C "$branch" "$jf_sha"
        fi
    fi

    output status "$status"
    output branch "$branch"
    output pr_number "$pr_number"
    output kind "$kind"
    output old_version "$old"
    output version "$new"
    output upstream_sha "$jf_sha"
    output conflicts "$conflicts"
    output companion_version "$(companion_version)"

    log "Result: ${status} (${old} -> ${new}, ${kind}) on ${branch}"
    [ -z "$conflicts" ] || log "Conflicting files: ${conflicts}"
}

publish() {
    # shellcheck disable=SC1090
    source "$STATE_FILE"
    case "${status:-}" in
        merged | conflict) ;;
        *) log "Nothing to publish (${status:-no state})."; return ;;
    esac
    local test_outcome="${TEST_OUTCOME:-skipped}"

    local draft=false reason=""
    if [ "$status" = conflict ]; then
        draft=true
        reason="merge conflict"
    elif [ "$test_outcome" != success ]; then
        draft=true
        reason="build or tests failed (${test_outcome})"
    fi

    local companion_major="" major_note=""
    if [ -n "${companion_version:-}" ]; then
        companion_major="${companion_version%%.*}"
        if [ "$companion_major" != "${version%%.*}" ]; then
            major_note="> [!WARNING]
> Major version mismatch: this repository would move to \`${version}\`, ${COMPANION_REPO} is on \`${companion_version}\`. Releases are blocked until both repositories track the same Jellyfin major version."
        fi
    fi

    local body
    body="$(mktemp)"
    {
        echo "Automatic sync with [${UPSTREAM_REPO}@${UPSTREAM_BRANCH}](https://github.com/${UPSTREAM_REPO}/tree/${UPSTREAM_BRANCH})."
        echo
        echo "| | |"
        echo "|---|---|"
        echo "| Jellyfin version | \`${old_version}\` → \`${version}\` (${kind}) |"
        echo "| Upstream commit | ${UPSTREAM_REPO}@${upstream_sha} |"
        echo "| Merge | ${status} |"
        echo "| Build + tests | ${test_outcome} |"
        echo "| Run | ${RUN_URL} |"
        echo
        if [ "$status" = conflict ]; then
            echo "### Conflicting files"
            echo
            for f in $conflicts; do echo "- \`${f}\`"; done
            echo
            echo "To resolve: \`git switch ${branch} && git merge origin/upstream && git merge origin/${BASE_BRANCH}\`, resolve, push to \`${branch}\`. **Merge, never rebase.** Later syncs update this PR instead of replacing it."
            echo
        fi
        [ -z "$major_note" ] || { echo "$major_note"; echo; }
        if [ "$draft" = true ]; then
            echo "**Draft PR: ${reason}.** ${NOTIFY}"
        elif [ "$kind" = patch ]; then
            echo "Patch version (\`x.y.Z\`): eligible for automatic merge, after review approval."
        else
            echo "Version change: ${kind}. Human review required, no automatic merge."
        fi
        echo
        echo "Merge with a **merge commit** only (no squash, no rebase) to keep the upstream ancestry."
    } > "$body"

    local title="Sync Jellyfin ${version}"
    [ "$draft" = false ] || title="${title}: ${reason}"

    if [ "$DRY_RUN" = true ]; then
        log "[dry run] PR '${title}' ${branch} -> ${BASE_BRANCH} (draft=${draft}, existing PR=${pr_number:-none})"
        cat "$body" >&2
        cat "$body" >> "${GITHUB_STEP_SUMMARY:-/dev/null}"
        [ "$draft" = false ] || { log "::error::${reason}"; exit 1; }
        return
    fi

    # Fast-forward push only: never force-push a sync branch.
    push "refs/heads/${branch}:refs/heads/${branch}"

    local url
    if [ -n "${pr_number:-}" ]; then
        url="$(gh pr view "$pr_number" --json url --jq .url)"
        gh pr edit "$pr_number" --title "$title" --body-file "$body"
        if [ "$draft" = true ]; then
            gh pr ready "$pr_number" --undo || true
        else
            gh pr ready "$pr_number" || true
        fi
        gh pr comment "$pr_number" --body "Updated by sync: Jellyfin \`${version}\`, merge ${status}, build + tests ${test_outcome}. ${RUN_URL}"
        log "PR updated: ${url}"
        summary "PR updated: ${url}"
    else
        local args=(--base "$BASE_BRANCH" --head "$branch" --title "$title" --body-file "$body")
        [ "$draft" = false ] || args+=(--draft)
        url="$(gh pr create "${args[@]}")"
        log "PR: ${url}"
        summary "PR: ${url}"
    fi

    if [ "$draft" = false ] && [ "$kind" = patch ] && [ "$AUTOMERGE" = true ]; then
        # GitHub auto-merge: the merge only happens once branch protection is
        # satisfied (green checks + required approval).
        gh pr merge "$url" --auto --merge
        summary "Auto-merge armed (patch version)."
    fi

    if [ "$draft" = true ]; then
        log "::error::Sync Jellyfin ${version}: ${reason}. Draft PR: ${url}"
        exit 1
    fi
}

case "${1:-}" in
    prepare) prepare ;;
    publish) publish ;;
    *) log "usage: $0 prepare|publish"; exit 2 ;;
esac
