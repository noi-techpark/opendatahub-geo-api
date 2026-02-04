FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY VectorTilesApi/VectorTilesApi.csproj VectorTilesApi/
COPY VectorTilesApi/. VectorTilesApi/

WORKDIR /src/VectorTilesApi

# Clean old artifacts
RUN rm -rf bin obj

RUN dotnet nuget locals all --clear
RUN dotnet restore

# Build without --no-restore
RUN dotnet build VectorTilesApi.csproj -c Release -o /app

FROM build AS publish
RUN dotnet publish VectorTilesApi.csproj -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Expose the API port
EXPOSE 80

ENTRYPOINT ["dotnet", "VectorTilesApi.dll"]
