#!/usr/bin/env bash
# Run everything from source (default mode). Opens the Aspire dashboard in your browser.
# Stop with Ctrl+C.  ⚠️  Do NOT have this running while you run `dotnet test` (tests start their own copy).
set -euo pipefail
cd "$(dirname "$0")/.."
dotnet run --project src/AspireNotifications.AppHost
