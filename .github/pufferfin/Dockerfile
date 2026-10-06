# syntax=docker/dockerfile:1
# PUFFERFIN: image ghcr.io/pufferfin/pufferfin = serveur Pufferfin + client
# Pufferfin-web compilé. Construite par .github/workflows/release.yml :
#   contexte principal : ce dépôt (serveur)
#   contexte nommé `web` : une copie de Pufferfin/Pufferfin-web
#   (les stages s'appellent *-build : un stage nommé `web` masquerait le
#   contexte `web` et BuildKit y verrait une dépendance circulaire)
ARG JELLYFIN_BASE_TAG
ARG DOTNET_SDK_TAG=10.0
ARG NODE_TAG=24

FROM --platform=$BUILDPLATFORM node:${NODE_TAG}-bookworm-slim AS web-build
WORKDIR /src
COPY --from=web package.json package-lock.json ./
RUN npm ci --no-audit
COPY --from=web . .
RUN npm run build:production

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:${DOTNET_SDK_TAG} AS server-build
ARG TARGETARCH
WORKDIR /src
COPY . .
RUN case "$TARGETARCH" in \
        amd64) rid=linux-x64 ;; \
        arm64) rid=linux-arm64 ;; \
        *) echo "Architecture non prise en charge : $TARGETARCH" >&2; exit 1 ;; \
    esac \
    && dotnet publish Jellyfin.Server/Jellyfin.Server.csproj \
        --configuration Release \
        --runtime "$rid" \
        --self-contained true \
        --output /out \
        -p:DebugSymbols=false -p:DebugType=none

# L'image officielle de la même version Jellyfin fournit jellyfin-ffmpeg,
# les pilotes et l'ENTRYPOINT ; seuls le serveur et le client sont remplacés.
FROM jellyfin/jellyfin:${JELLYFIN_BASE_TAG}
RUN rm -rf /jellyfin
COPY --from=server-build /out /jellyfin
COPY --from=web-build /src/dist /jellyfin/jellyfin-web
