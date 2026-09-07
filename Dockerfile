FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/TrackZ.Api/TrackZ.Api.csproj src/TrackZ.Api/
COPY src/TrackZ.Application/TrackZ.Application.csproj src/TrackZ.Application/
COPY src/TrackZ.Contracts/TrackZ.Contracts.csproj src/TrackZ.Contracts/
COPY src/TrackZ.Domain/TrackZ.Domain.csproj src/TrackZ.Domain/
COPY src/TrackZ.Infrastructure/TrackZ.Infrastructure.csproj src/TrackZ.Infrastructure/
RUN dotnet restore src/TrackZ.Api/TrackZ.Api.csproj --runtime linux-x64

COPY src/ src/
RUN dotnet publish src/TrackZ.Api/TrackZ.Api.csproj \
    --configuration Release \
    --runtime linux-x64 \
    --self-contained false \
    --no-restore \
    --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
USER root
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build --chown=app:app /app/publish/ ./
COPY --chown=app:app assets/exercises/ ./assets/exercises/

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER app
ENTRYPOINT ["dotnet", "TrackZ.Api.dll"]
