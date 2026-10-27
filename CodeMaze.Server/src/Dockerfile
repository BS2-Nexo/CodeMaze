ARG DOTNET_VERSION=10.0

# ---- build ----
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
WORKDIR /source

COPY . .
RUN dotnet restore CodeMaze.Server/src/CodeMaze.Server.csproj
RUN dotnet publish CodeMaze.Server/src/CodeMaze.Server.csproj \
    -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "CodeMaze.Server.dll"]