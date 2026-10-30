# The build context is the repo root:  docker build -f src/Bubble.Relay/Dockerfile .
# Runs on amd64 and arm64.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY src/Bubble.Core/Bubble.Core.csproj src/Bubble.Core/
COPY src/Bubble.Relay/Bubble.Relay.csproj src/Bubble.Relay/
RUN dotnet restore src/Bubble.Relay -a $TARGETARCH
COPY src/Bubble.Core/ src/Bubble.Core/
COPY src/Bubble.Relay/ src/Bubble.Relay/
RUN dotnet publish src/Bubble.Relay -a $TARGETARCH -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
RUN mkdir /data && chown $APP_UID /data
ENV ASPNETCORE_HTTP_PORTS=5080 \
    Relay__Database=/data/relay.db
VOLUME /data
EXPOSE 5080
USER $APP_UID
ENTRYPOINT ["dotnet", "Bubble.Relay.dll"]
