# Build context = the REPO ROOT, e.g.:
#   docker build -f src/AspireNotifications.CustomerInterface/Dockerfile -t customer-interface .
#   az acr build --registry <acr> --image customer-interface:v1 -f src/AspireNotifications.CustomerInterface/Dockerfile .

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/AspireNotifications.Contracts/         src/AspireNotifications.Contracts/
COPY src/AspireNotifications.ServiceDefaults/   src/AspireNotifications.ServiceDefaults/
COPY src/AspireNotifications.CustomerInterface/ src/AspireNotifications.CustomerInterface/

RUN dotnet publish src/AspireNotifications.CustomerInterface -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "AspireNotifications.CustomerInterface.dll"]
