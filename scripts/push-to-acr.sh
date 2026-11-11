#!/usr/bin/env bash
# =====================================================================================
#  Build the two service images IN AZURE (no local Docker build needed) and push them
#  to your Azure Container Registry. Then run the AppHost in "Acr" mode.
#
#  Usage:
#     ./scripts/push-to-acr.sh <registry-name> [tag]
#     ./scripts/push-to-acr.sh myacr v1
#
#  One-time setup (if you don't have a registry yet):
#     az login
#     az group create  --name rg-aspire-notifications --location uksouth
#     az acr create    --name myacr --resource-group rg-aspire-notifications --sku Basic
# =====================================================================================
set -euo pipefail

REGISTRY_NAME="${1:?Usage: $0 <registry-name> [tag]}"
TAG="${2:-latest}"
REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"

cd "$REPO_ROOT"

echo "▶ Building notification-reader:$TAG in ACR '$REGISTRY_NAME'..."
az acr build \
  --registry "$REGISTRY_NAME" \
  --image "notification-reader:$TAG" \
  --file src/AspireNotifications.NotificationReader/Dockerfile \
  .

echo "▶ Building customer-interface:$TAG in ACR '$REGISTRY_NAME'..."
az acr build \
  --registry "$REGISTRY_NAME" \
  --image "customer-interface:$TAG" \
  --file src/AspireNotifications.CustomerInterface/Dockerfile \
  .

echo
echo "✅ Done. To run the AppHost with these images:"
echo
echo "   az acr login --name $REGISTRY_NAME        # lets Docker pull from the registry"
echo "   dotnet run --project src/AspireNotifications.AppHost -- \\"
echo "       --Images:Source=Acr \\"
echo "       --Images:Registry=$REGISTRY_NAME.azurecr.io \\"
echo "       --Images:Tag=$TAG"
