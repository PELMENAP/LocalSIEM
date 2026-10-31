#!/usr/bin/env bash
set -euo pipefail
cd "$(dirname "$0")/.."

if [[ ! -f .env ]]; then
  cp .env.example .env
  echo "Создан файл .env. Заполните POSTGRES_PASSWORD и LOCALSIEM_ADMIN_PASSWORD и запустите скрипт снова."
  exit 1
fi

set -a
source .env
set +a

: "${POSTGRES_PASSWORD:?В .env не задан POSTGRES_PASSWORD}"

if [[ -z "${LOCALSIEM_JWT_KEY:-}" ]]; then
  LOCALSIEM_JWT_KEY="$(head -c 48 /dev/urandom | base64 | tr -d '\n')"
  sed -i.bak "s|^LOCALSIEM_JWT_KEY=.*$|LOCALSIEM_JWT_KEY=${LOCALSIEM_JWT_KEY}|" .env && rm -f .env.bak
  echo "Сгенерирован LOCALSIEM_JWT_KEY и сохранён в .env"
fi

docker compose up -d --wait

export ConnectionStrings__Postgres="Host=localhost;Port=${POSTGRES_PORT:-5432};Database=${POSTGRES_DB:-localsiem};Username=${POSTGRES_USER:-localsiem};Password=${POSTGRES_PASSWORD}"
export Jwt__Key="${LOCALSIEM_JWT_KEY}"
if [[ -n "${LOCALSIEM_ADMIN_PASSWORD:-}" ]]; then
  export Seed__AdminPassword="${LOCALSIEM_ADMIN_PASSWORD}"
fi

if [[ ! -d src/LocalSIEM.Infrastructure/Persistence/Migrations ]]; then
  ./scripts/create-migration.sh
fi

if [[ ! -f src/LocalSIEM.Api/wwwroot/index.html ]]; then
  npm --prefix web install
  npm --prefix web run build
fi

echo "LocalSIEM: http://localhost:5080"
dotnet run --project src/LocalSIEM.Api --launch-profile LocalSIEM.Api
