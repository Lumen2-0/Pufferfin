# syntax=docker/dockerfile:1.7
# Pufferfin: server (.NET) + web client in one image, for Dokploy or any Docker host.
# The web client lives in a separate private repo: it is cloned at build time.
#   --build-arg GITHUB_TOKEN=<token with read access to the web repo>   (private repo)
#   --build-arg WEB_REF=<branch or tag of the web client>               (default: main)

ARG WEB_REPO=https://github.com/Lumen2-0/Pufferfin-web.git
ARG WEB_REF=main

# ---- Web client ----------------------------------------------------------------------------
FROM node:24-bookworm-slim AS web
ARG WEB_REPO
ARG WEB_REF
ARG GITHUB_TOKEN=
RUN apt-get update && apt-get install -y --no-install-recommends git ca-certificates \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /web
# The token only lives in this build stage; it never reaches the final image.
RUN URL="$WEB_REPO"; \
    if [ -n "$GITHUB_TOKEN" ]; then URL="https://x-access-token:${GITHUB_TOKEN}@${WEB_REPO#https://}"; fi; \
    git clone --depth 1 --branch "$WEB_REF" "$URL" . && rm -rf .git
RUN npm ci --no-audit --no-fund && npm run build:production

# ---- Server --------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS server
WORKDIR /src
COPY . .
RUN dotnet publish Jellyfin.Server -c Release -o /app --no-self-contained

# ---- Runtime -------------------------------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends \
        ffmpeg libfontconfig1 libfreetype6 libssl3 curl ca-certificates \
    && rm -rf /var/lib/apt/lists/*
COPY --from=server /app /jellyfin
COPY --from=web /web/dist /jellyfin/jellyfin-web

ENV JELLYFIN_DATA_DIR=/config \
    JELLYFIN_CONFIG_DIR=/config/config \
    JELLYFIN_LOG_DIR=/config/log \
    JELLYFIN_CACHE_DIR=/cache \
    JELLYFIN_WEB_DIR=/jellyfin/jellyfin-web \
    JELLYFIN_FFMPEG=/usr/bin/ffmpeg \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1

VOLUME ["/config", "/cache"]
EXPOSE 8096
HEALTHCHECK --interval=30s --timeout=5s --start-period=60s --retries=5 \
    CMD curl -fsS http://localhost:8096/health || exit 1
WORKDIR /jellyfin
ENTRYPOINT ["dotnet", "jellyfin.dll"]
