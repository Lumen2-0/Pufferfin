#!/usr/bin/env bash
# PUFFERFIN: version de release et garde-fou d'alignement serveur / client.
# Contrat : .github/pufferfin/README.md
#
# Usage : release-version.sh <dossier Pufferfin-web>
# Sorties : jellyfin_version, web_version, number, version, base_tag
#
# Bloque (code 1) si serveur et client ne suivent pas la même version majeure
# de Jellyfin.
set -euo pipefail

WEB_DIR="${1:?usage: $0 <dossier Pufferfin-web>}"
re='^([0-9]+)\.([0-9]+)\.([0-9]+)$'

server="$(sed -n 's/.*AssemblyVersion("\([^"]*\)").*/\1/p' SharedVersion.cs | head -n 1)"
web="$(sed -n 's/^  "version": "\([^"]*\)".*/\1/p' "${WEB_DIR}/package.json" | head -n 1)"

[[ "$server" =~ $re ]] || { echo "::error::Version serveur illisible : '${server}'" >&2; exit 1; }
[[ "$web" =~ $re ]] || { echo "::error::Version client illisible : '${web}'" >&2; exit 1; }

if [ "${server%%.*}" != "${web%%.*}" ]; then
    echo "::error::Release bloquée : serveur en Jellyfin ${server}, client en Jellyfin ${web}. Les deux dépôts doivent suivre la même version majeure." >&2
    exit 1
fi

# <n> : 1 pour la première image d'une version Jellyfin, puis +1 à chaque release.
last="$(git tag -l "${server}-pufferfin.*" | sed -n "s/^${server//./\\.}-pufferfin\.\([0-9][0-9]*\)$/\1/p" | sort -n | tail -n 1)"
number=$(( ${last:-0} + 1 ))
version="${server}-pufferfin.${number}"

# Image de base : l'image officielle Jellyfin de la même version (x.y), qui
# apporte jellyfin-ffmpeg et les dépendances système.
base_tag="$(cut -d. -f1-2 <<< "$server")"

{
    echo "jellyfin_version=${server}"
    echo "web_version=${web}"
    echo "number=${number}"
    echo "version=${version}"
    echo "base_tag=${base_tag}"
} | tee -a "${GITHUB_OUTPUT:-/dev/null}"
