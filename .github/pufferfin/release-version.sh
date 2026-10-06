#!/usr/bin/env bash
# PUFFERFIN: release version and server / client alignment guard.
# Contract: .github/pufferfin/README.md
#
# Usage: release-version.sh <Pufferfin-web directory>
# Outputs: jellyfin_version, web_version, number, version, base_tag
#
# Fails (exit 1) when server and client do not track the same Jellyfin major
# version.
set -euo pipefail

WEB_DIR="${1:?usage: $0 <Pufferfin-web directory>}"
re='^([0-9]+)\.([0-9]+)\.([0-9]+)$'

server="$(sed -n 's/.*AssemblyVersion("\([^"]*\)").*/\1/p' SharedVersion.cs | head -n 1)"
web="$(sed -n 's/^  "version": "\([^"]*\)".*/\1/p' "${WEB_DIR}/package.json" | head -n 1)"

[[ "$server" =~ $re ]] || { echo "::error::Unreadable server version: '${server}'" >&2; exit 1; }
[[ "$web" =~ $re ]] || { echo "::error::Unreadable client version: '${web}'" >&2; exit 1; }

if [ "${server%%.*}" != "${web%%.*}" ]; then
    echo "::error::Release blocked: server is on Jellyfin ${server}, client is on Jellyfin ${web}. Both repositories must track the same major version." >&2
    exit 1
fi

# <n>: 1 for the first image of a Jellyfin version, then +1 for each release.
last="$(git tag -l "${server}-pufferfin.*" | sed -n "s/^${server//./\\.}-pufferfin\.\([0-9][0-9]*\)$/\1/p" | sort -n | tail -n 1)"
number=$(( ${last:-0} + 1 ))
version="${server}-pufferfin.${number}"

# Base image: the official Jellyfin image of the same version (x.y), which
# provides jellyfin-ffmpeg and the system dependencies.
base_tag="$(cut -d. -f1-2 <<< "$server")"

{
    echo "jellyfin_version=${server}"
    echo "web_version=${web}"
    echo "number=${number}"
    echo "version=${version}"
    echo "base_tag=${base_tag}"
} | tee -a "${GITHUB_OUTPUT:-/dev/null}"
