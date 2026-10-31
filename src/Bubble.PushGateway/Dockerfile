# The build context is the repo root:  docker build -f src/Bubble.PushGateway/Dockerfile .
# Runs on amd64 and arm64.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src
COPY src/Bubble.PushGateway/Bubble.PushGateway.csproj src/Bubble.PushGateway/
RUN dotnet restore src/Bubble.PushGateway -a $TARGETARCH
COPY src/Bubble.PushGateway/ src/Bubble.PushGateway/
RUN dotnet publish src/Bubble.PushGateway -a $TARGETARCH -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=5090
EXPOSE 5090
USER $APP_UID
ENTRYPOINT ["dotnet", "Bubble.PushGateway.dll"]
