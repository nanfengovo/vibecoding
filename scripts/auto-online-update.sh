#!/usr/bin/env bash

set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

BACKEND_PORT="${BACKEND_PORT:-15000}"
FRONTEND_PORT="${FRONTEND_PORT:-18080}"
DB_PORT="${DB_PORT:-21433}"

echo "[auto-online-update] start at $(date '+%Y-%m-%d %H:%M:%S')"
echo "[auto-online-update] ports: backend=${BACKEND_PORT}, frontend=${FRONTEND_PORT}, db=${DB_PORT}"

BACKEND_PORT="$BACKEND_PORT" FRONTEND_PORT="$FRONTEND_PORT" DB_PORT="$DB_PORT" docker compose up -d --build backend frontend

echo "[auto-online-update] done at $(date '+%Y-%m-%d %H:%M:%S')"
