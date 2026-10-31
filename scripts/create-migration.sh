#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."
NAME="${1:-InitialCreate}"
dotnet tool restore
dotnet ef migrations add "$NAME" \
  --project src/LocalSIEM.Infrastructure \
  --startup-project src/LocalSIEM.Api \
  --output-dir Persistence/Migrations
