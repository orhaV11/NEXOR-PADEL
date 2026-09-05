# OREVOSH in one container: built with the .NET SDK, run on the ASP.NET runtime as the image's non-root "app" user, with
# the database and the photo/clip folder on the /data volume. docker-compose.yml puts Caddy in front for HTTPS. See DEPLOY.md.

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
# Restore on its own layer so packages are cached until the project file changes.
COPY src/FitCheck.Api/FitCheck.Api.csproj src/FitCheck.Api/
RUN dotnet restore src/FitCheck.Api/FitCheck.Api.csproj
COPY src/FitCheck.Api/ src/FitCheck.Api/
RUN dotnet publish src/FitCheck.Api/FitCheck.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
# curl is for the HEALTHCHECK only; the runtime image ships without it. /data is created here so the named volume made
# from it belongs to the app user.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /data \
    && chown app:app /data
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080 \
    ConnectionStrings__Default="Data Source=/data/orevosh.db" \
    Storage__Root=/data/storage \
    DOTNET_EnableDiagnostics=0
VOLUME /data
EXPOSE 8080
USER app
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl -fsS http://127.0.0.1:8080/healthz || exit 1
ENTRYPOINT ["dotnet", "FitCheck.Api.dll"]
