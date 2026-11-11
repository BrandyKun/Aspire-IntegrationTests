# Build context = the REPO ROOT (the folder with Directory.Build.props), e.g.:
#   docker build -f src/AspireNotifications.NotificationReader/Dockerfile -t notification-reader .
#   az acr build --registry <acr> --image notification-reader:v1 -f src/AspireNotifications.NotificationReader/Dockerfile .

# ---- stage 1: compile with the SDK image ---------------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Shared build settings first (good layer caching).
COPY Directory.Build.props Directory.Packages.props global.json ./

# Only the projects this service needs.
COPY src/AspireNotifications.Contracts/        src/AspireNotifications.Contracts/
COPY src/AspireNotifications.ServiceDefaults/  src/AspireNotifications.ServiceDefaults/
COPY src/AspireNotifications.NotificationReader/ src/AspireNotifications.NotificationReader/

RUN dotnet publish src/AspireNotifications.NotificationReader -c Release -o /app

# ---- stage 2: tiny runtime image ------------------------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# The aspnet image listens on 8080 by default. The AppHost maps this port (see AddContainer).
EXPOSE 8080
ENTRYPOINT ["dotnet", "AspireNotifications.NotificationReader.dll"]
