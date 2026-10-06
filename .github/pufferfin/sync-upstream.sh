#!/usr/bin/env bash
# PUFFERFIN: synchronisation avec l'amont Jellyfin. Fichier propre à Pufferfin,
# identique dans Pufferfin/Pufferfin et Pufferfin/Pufferfin-web.
# Contrat : .github/pufferfin/README.md
#
#   sync-upstream.sh prepare   met à jour `upstream`, fusionne dans sync/<date>
#   sync-upstream.sh publish   pousse sync/<date> et ouvre la PR (brouillon si
#                              conflit ou tests rouges)
#
# Environnement :
#   UPSTREAM_REPO     dépôt Jellyfin suivi (jellyfin/jellyfin, jellyfin/jellyfin-web)
#   UPSTREAM_BRANCH   branche de version suivie (release-12.z)
#   UPSTREAM_URL      URL git de l'amont (par défaut https://github.com/$UPSTREAM_REPO.git)
#   BASE_BRANCH       branche Pufferfin cible (main)
#   COMPANION_REPO    l'autre dépôt Pufferfin, pour le garde-fou de version majeure
#   DRY_RUN           true : ne pousse rien, n'ouvre aucune PR
#   WATCH_ONLY        true : ne synchronise que si Jellyfin a publié une nouvelle version
#   AUTOMERGE         true : arme l'auto-merge des versions correctives x.y.Z
#   NOTIFY            mentions ajoutées aux PR en échec (ex. "@Pufferfin/maintainers")
#   TEST_OUTCOME      (publish) résultat de l'étape build + tests : success | failure
#   STATE_FILE        fichier d'état partagé entre prepare et publish
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
    # Sortie d'étape GitHub Actions + état pour l'étape publish.
    printf '%s=%s\n' "$1" "$2" >> "${GITHUB_OUTPUT:-/dev/null}"
    printf '%s=%q\n' "$1" "$2" >> "$STATE_FILE"
}

summary() {
    printf '%s\n' "$*" >> "${GITHUB_STEP_SUMMARY:-/dev/null}"
}

# Version Jellyfin portée par un arbre : SharedVersion.cs (serveur) ou package.json (web).
jellyfin_version() {
    local ref="$1"
    if git cat-file -e "${ref}:SharedVersion.cs" 2>/dev/null; then
        git show "${ref}:SharedVersion.cs" | sed -n 's/.*AssemblyVersion("\([^"]*\)").*/\1/p' | head -n 1
    else
        git show "${ref}:package.json" | sed -n 's/^  "version": "\([^"]*\)".*/\1/p' | head -n 1
    fi
}

# Nature du passage d'une version à l'autre : patch | minor | major | none | unknown.
# Seul `patch` (x.y.Z, même x.y, Z croissant) autorise la fusion automatique.
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

# Version de l'autre dépôt Pufferfin, lue sur sa branche main.
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

# PR de sync ouverte vers BASE_BRANCH : "<numéro> <branche>", la plus ancienne.
open_sync_pr() {
    gh pr list --base "$BASE_BRANCH" --state open --json number,headRefName \
        --jq '[.[] | select(.headRefName | startswith("sync/"))] | sort_by(.number) | first | select(. != null) | "\(.number) \(.headRefName)"'
}

push() {
    if [ "$DRY_RUN" = true ]; then
        log "[à blanc] git push origin $*"
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
        log "::error::La branche 'upstream' n'existe pas : elle s'amorce à la main (voir .github/pufferfin/README.md)."
        exit 1
    fi
    git fetch --no-tags "$UPSTREAM_URL" "+refs/heads/${UPSTREAM_BRANCH}:${JF_REF}"

    local jf_sha up_sha
    jf_sha="$(git rev-parse "$JF_REF")"
    up_sha="$(git rev-parse refs/remotes/origin/upstream)"

    # 1. `upstream` = copie exacte de l'amont, en avance rapide uniquement.
    if ! git merge-base --is-ancestor "$up_sha" "$jf_sha"; then
        log "::error::${UPSTREAM_REPO}@${UPSTREAM_BRANCH} n'est pas un descendant de 'upstream' (historique amont réécrit ?). Rien n'est forcé : intervention humaine requise."
        exit 1
    fi

    local old new up_version kind
    old="$(jellyfin_version "refs/remotes/origin/${BASE_BRANCH}")"
    new="$(jellyfin_version "$JF_REF")"
    up_version="$(jellyfin_version refs/remotes/origin/upstream)"

    if [ "$WATCH_ONLY" = true ] && [ "$new" = "$up_version" ]; then
        log "Veille : pas de nouvelle version Jellyfin (${new}), rien à faire."
        output status idle
        summary "Veille : pas de nouvelle version Jellyfin (\`${new}\`)."
        return
    fi

    if [ "$jf_sha" != "$up_sha" ]; then
        log "upstream : ${up_sha:0:10} -> ${jf_sha:0:10} (${UPSTREAM_REPO}@${UPSTREAM_BRANCH})"
        push "${jf_sha}:refs/heads/upstream"
    fi

    # 2. Rien à fusionner ?
    if git merge-base --is-ancestor "$jf_sha" "refs/remotes/origin/${BASE_BRANCH}"; then
        log "${BASE_BRANCH} contient déjà ${UPSTREAM_REPO}@${jf_sha:0:10} (${new})."
        output status up-to-date
        output version "$new"
        summary "\`${BASE_BRANCH}\` est à jour avec Jellyfin \`${new}\` (\`${jf_sha:0:10}\`)."
        return
    fi

    kind="$(classify "$old" "$new")"
    local branch pr_number="" status conflicts=""

    # Une PR de sync déjà ouverte est mise à jour, jamais remplacée : elle peut
    # porter une résolution de conflit ou une approbation.
    local existing
    existing="$(open_sync_pr)"
    if [ -n "$existing" ]; then
        read -r pr_number branch <<< "$existing"
        git fetch --no-tags origin "+refs/heads/${branch}:refs/remotes/origin/${branch}"
        if git merge-base --is-ancestor "$jf_sha" "refs/remotes/origin/${branch}"; then
            log "La PR #${pr_number} (${branch}) contient déjà ${UPSTREAM_REPO}@${jf_sha:0:10} : rien à faire."
            output status pending
            summary "La PR de sync #${pr_number} (\`${branch}\`) couvre déjà Jellyfin \`${new}\`."
            return
        fi

        # 3a. L'amont a encore avancé : on le fusionne dans la branche existante,
        # puis main si besoin. Un conflit laisse la branche telle quelle.
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

        # 3b. Fusion (merge, jamais rebase) de l'amont dans sync/<date> partant de main.
        git switch -q -C "$branch" "refs/remotes/origin/${BASE_BRANCH}"
        if git merge --no-ff --no-edit \
            -m "Merge Jellyfin ${new} (${UPSTREAM_REPO}@${UPSTREAM_BRANCH}) into ${BASE_BRANCH}" \
            "$jf_sha"; then
            status=merged
        else
            status=conflict
            conflicts="$(git diff --name-only --diff-filter=U | tr '\n' ' ')"
            git merge --abort
            # La branche pointe sur l'amont : la PR affiche le conflit, et la
            # résolution se fait par un merge de main dans cette branche.
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

    log "Résultat : ${status} (${old} -> ${new}, ${kind}) sur ${branch}"
    [ -z "$conflicts" ] || log "Fichiers en conflit : ${conflicts}"
}

publish() {
    # shellcheck disable=SC1090
    source "$STATE_FILE"
    case "${status:-}" in
        merged | conflict) ;;
        *) log "Rien à publier (${status:-aucun état})."; return ;;
    esac
    local test_outcome="${TEST_OUTCOME:-skipped}"

    local draft=false reason=""
    if [ "$status" = conflict ]; then
        draft=true
        reason="conflit de fusion"
    elif [ "$test_outcome" != success ]; then
        draft=true
        reason="compilation ou tests en échec (${test_outcome})"
    fi

    local companion_major="" major_note=""
    if [ -n "${companion_version:-}" ]; then
        companion_major="${companion_version%%.*}"
        if [ "$companion_major" != "${version%%.*}" ]; then
            major_note="> [!WARNING]
> Version majeure désalignée : ce dépôt passerait en \`${version}\`, ${COMPANION_REPO} est en \`${companion_version}\`. La release est bloquée tant que les deux dépôts ne suivent pas la même version majeure de Jellyfin."
        fi
    fi

    local body
    body="$(mktemp)"
    {
        echo "Synchronisation automatique avec [${UPSTREAM_REPO}@${UPSTREAM_BRANCH}](https://github.com/${UPSTREAM_REPO}/tree/${UPSTREAM_BRANCH})."
        echo
        echo "| | |"
        echo "|---|---|"
        echo "| Version Jellyfin | \`${old_version}\` → \`${version}\` (${kind}) |"
        echo "| Commit amont | ${UPSTREAM_REPO}@${upstream_sha} |"
        echo "| Fusion | ${status} |"
        echo "| Build + tests | ${test_outcome} |"
        echo "| Exécution | ${RUN_URL} |"
        echo
        if [ "$status" = conflict ]; then
            echo "### Fichiers en conflit"
            echo
            for f in $conflicts; do echo "- \`${f}\`"; done
            echo
            echo "Pour résoudre : \`git switch ${branch} && git merge origin/upstream && git merge origin/${BASE_BRANCH}\`, résoudre, pousser sur \`${branch}\`. **Merge, jamais rebase.** Les syncs suivantes mettent à jour cette PR sans la remplacer."
            echo
        fi
        [ -z "$major_note" ] || { echo "$major_note"; echo; }
        if [ "$draft" = true ]; then
            echo "**PR en brouillon : ${reason}.** ${NOTIFY}"
        elif [ "$kind" = patch ]; then
            echo "Version corrective (\`x.y.Z\`) : éligible à la fusion automatique, après approbation de la relecture."
        else
            echo "Version ${kind} : relecture humaine requise, pas de fusion automatique."
        fi
        echo
        echo "À fusionner en **merge commit** uniquement (ni squash, ni rebase) pour conserver la généalogie amont."
    } > "$body"

    local title="Sync Jellyfin ${version}"
    [ "$draft" = false ] || title="${title} — ${reason}"

    if [ "$DRY_RUN" = true ]; then
        log "[à blanc] PR '${title}' ${branch} -> ${BASE_BRANCH} (brouillon=${draft}, PR existante=${pr_number:-aucune})"
        cat "$body" >&2
        cat "$body" >> "${GITHUB_STEP_SUMMARY:-/dev/null}"
        [ "$draft" = false ] || { log "::error::${reason}"; exit 1; }
        return
    fi

    # Push en avance rapide uniquement : jamais de force sur une branche de sync.
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
        gh pr comment "$pr_number" --body "Mise à jour par la sync : Jellyfin \`${version}\`, fusion ${status}, build + tests ${test_outcome}. ${RUN_URL}"
        log "PR mise à jour : ${url}"
        summary "PR mise à jour : ${url}"
    else
        local args=(--base "$BASE_BRANCH" --head "$branch" --title "$title" --body-file "$body")
        [ "$draft" = false ] || args+=(--draft)
        url="$(gh pr create "${args[@]}")"
        log "PR : ${url}"
        summary "PR : ${url}"
    fi

    if [ "$draft" = false ] && [ "$kind" = patch ] && [ "$AUTOMERGE" = true ]; then
        # Auto-merge GitHub : la fusion n'a lieu qu'une fois les protections de
        # branche satisfaites (checks verts + approbation requise).
        gh pr merge "$url" --auto --merge
        summary "Auto-merge armé (version corrective)."
    fi

    if [ "$draft" = true ]; then
        log "::error::Sync Jellyfin ${version} : ${reason}. PR en brouillon : ${url}"
        exit 1
    fi
}

case "${1:-}" in
    prepare) prepare ;;
    publish) publish ;;
    *) log "usage: $0 prepare|publish"; exit 2 ;;
esac
