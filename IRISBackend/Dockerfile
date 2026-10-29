FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build
WORKDIR /src
COPY . .
RUN dotnet restore IRISBackend/IRISBackend.csproj
RUN dotnet publish IRISBackend/IRISBackend.csproj -c Release --no-restore -o /out

FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl libicu72 \
    && rm -rf /var/lib/apt/lists/* \
    && mkdir -p /app/App_Data/Uploads /app/App_Data/Documents /app/App_Data/Keys \
    && chown -R "$APP_UID:$APP_UID" /app
COPY --from=build /out .
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
    CMD curl --fail --silent http://localhost:8080/health/ready || exit 1
ENTRYPOINT ["dotnet", "IRISBackend.dll"]
