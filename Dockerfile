# Build desde la raíz del repositorio:
#   docker-compose build

# ─── Etapa 1: compilación ─────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
WORKDIR /src

COPY DateroWebG/DateroWebG.csproj DateroWebG/
RUN dotnet restore DateroWebG/DateroWebG.csproj -r linux-x64

COPY DateroWebG/ DateroWebG/
RUN dotnet publish DateroWebG/DateroWebG.csproj \
    -c Release -r linux-x64 --self-contained false --no-restore \
    -p:UseAppHost=false -o /app/publish

# ─── Etapa 2: ejecución ───────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble AS runtime

ENV TZ=America/Lima \
    DEBIAN_FRONTEND=noninteractive \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DATERO_App__PuertoHttp=8080

RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata curl ca-certificates \
    && ln -snf /usr/share/zoneinfo/$TZ /etc/localtime && echo $TZ > /etc/timezone \
    && rm -rf /var/lib/apt/lists/* \
    && groupadd --gid 10001 appuser \
    && useradd --uid 10001 --gid 10001 --no-create-home --shell /usr/sbin/nologin appuser

WORKDIR /app
COPY --from=build --chown=10001:10001 /app/publish .

USER 10001:10001

EXPOSE 8080

HEALTHCHECK --interval=30s --timeout=5s --start-period=20s --retries=3 \
    CMD curl -fsS http://127.0.0.1:8080/health || exit 1

ENTRYPOINT ["dotnet", "DateroWebG.dll"]
