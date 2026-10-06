# syntax=docker/dockerfile:1
# PUFFERFIN: image ghcr.io/pufferfin/pufferfin = Pufferfin server + built
# Pufferfin-web client. Built by .github/workflows/release.yml:
#   main context: this repository (server)
#   named context `web`: a checkout of Pufferfin/Pufferfin-web
#   (stages are named *-build: a stage named `web` would shadow the `web`
#   context and BuildKit would report a circular dependency)
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
        *) echo "Unsupported architecture: $TARGETARCH" >&2; exit 1 ;; \
    esac \
    && dotnet publish Jellyfin.Server/Jellyfin.Server.csproj \
        --configuration Release \
        --runtime "$rid" \
        --self-contained true \
        --output /out \
        -p:DebugSymbols=false -p:DebugType=none

# The official image of the same Jellyfin version provides jellyfin-ffmpeg,
# the drivers and the ENTRYPOINT; only the server and the client are replaced.
FROM jellyfin/jellyfin:${JELLYFIN_BASE_TAG}
RUN rm -rf /jellyfin
COPY --from=server-build /out /jellyfin
COPY --from=web-build /src/dist /jellyfin/jellyfin-web
