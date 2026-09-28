# syntax=docker/dockerfile:1
#
# Family Hub – serveren som container.
#
#   docker compose up -d --build        (se docker-compose.yml og .env.example)
#
# Bygger til den maskine, der bygger (x64 eller arm64). Krydsbygning, fx fra en x64-pc til en arm64-server:
#   docker buildx build --platform linux/arm64 -t familyhub:latest .

ARG DOTNET_VERSION=10.0

# ---------- Byg ----------
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG TARGETARCH
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/ src/

# NuGet-pakkerne gemmes mellem builds (cache-mount), så en opdatering kun henter det nye.
RUN --mount=type=cache,id=familyhub-nuget,target=/root/.nuget/packages \
    dotnet restore src/FamilyHub.Web/FamilyHub.Web.csproj -a "$TARGETARCH"

RUN --mount=type=cache,id=familyhub-nuget,target=/root/.nuget/packages \
    dotnet publish src/FamilyHub.Web/FamilyHub.Web.csproj \
        -c Release -a "$TARGETARCH" --self-contained false --no-restore \
        -o /app -p:DebugType=none --nologo

# ---------- Kør ----------
# Det almindelige (Ubuntu-baserede) aspnet-image: har ICU (dansk dato/tal-format) og tzdata.
# Ikke chiseled/Alpine – de mangler ICU.
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final

# curl bruges kun af healthchecken.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

# Datamappen (husstand, nøgler, kalender, senere databaser). Monteres som volumen af docker-compose.yml.
RUN mkdir -p /data && chown "$APP_UID:$APP_UID" /data

ENV FamilyHub__DataDirectory=/data \
    ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_NOLOGO=1 \
    TZ=Europe/Copenhagen

# Kør som den indbyggede, upriviligerede bruger "app" (UID 1654).
USER $APP_UID
EXPOSE 8080

HEALTHCHECK --interval=10s --timeout=5s --start-period=60s --retries=3 \
    CMD curl -fsS http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "FamilyHub.Web.dll"]
