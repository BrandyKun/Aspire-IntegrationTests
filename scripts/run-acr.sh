#!/usr/bin/env bash
# Run the pipeline using the images from Azure Container Registry.
#   ./scripts/run-acr.sh <registry-name> [tag]
set -euo pipefail
REGISTRY_NAME="${1:?Usage: $0 <registry-name> [tag]}"
TAG="${2:-latest}"
cd "$(dirname "$0")/.."

az acr login --name "$REGISTRY_NAME"

dotnet run --project src/AspireNotifications.AppHost -- \
  --Images:Source=Acr \
  --Images:Registry="$REGISTRY_NAME.azurecr.io" \
  --Images:Tag="$TAG"
