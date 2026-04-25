#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
FRONTEND_DIR="$ROOT_DIR/frontend"
TUNNEL_LOG="${TUNNEL_LOG:-/tmp/quanttrading-cloudflared.log}"
BACKEND_LOCAL_URL="${BACKEND_LOCAL_URL:-http://localhost:15000}"
CHECK_INTERVAL_SECONDS="${CHECK_INTERVAL_SECONDS:-30}"

if ! command -v cloudflared >/dev/null 2>&1; then
  echo "[keep-online] cloudflared is required but not found." >&2
  exit 1
fi

if ! command -v vercel >/dev/null 2>&1; then
  echo "[keep-online] vercel cli is required but not found." >&2
  exit 1
fi

wait_backend_up() {
  for _ in {1..30}; do
    if curl -fsS -m 4 "$BACKEND_LOCAL_URL/health" >/dev/null 2>&1; then
      return 0
    fi
    sleep 1
  done

  echo "[keep-online] backend is not reachable at $BACKEND_LOCAL_URL" >&2
  return 1
}

start_tunnel() {
  extract_tunnel_url() {
    tail -n 300 "$TUNNEL_LOG" \
      | awk 'match($0, /https:\/\/[-a-z0-9]+[.]trycloudflare[.]com/) { candidate = substr($0, RSTART, RLENGTH); if (candidate != "https://api.trycloudflare.com") url = candidate } END { if (url != "") print url }'
  }

  pkill -f "cloudflared tunnel --url $BACKEND_LOCAL_URL" >/dev/null 2>&1 || true
  : >"$TUNNEL_LOG"
  nohup cloudflared tunnel \
    --url "$BACKEND_LOCAL_URL" \
    --protocol quic \
    --edge-ip-version 4 \
    --no-autoupdate \
    >"$TUNNEL_LOG" 2>&1 &

  for _ in {1..45}; do
    url="$(extract_tunnel_url || true)"
    if [[ -n "${url:-}" ]]; then
      echo "$url"
      return 0
    fi
    sleep 1
  done

  # One last parse attempt before giving up (log buffering can delay first visible line).
  url="$(extract_tunnel_url || true)"
  if [[ -n "${url:-}" ]]; then
    echo "$url"
    return 0
  fi

  echo "[keep-online] failed to create trycloudflare url" >&2
  tail -n 60 "$TUNNEL_LOG" >&2 || true
  return 1
}

sync_vercel_env_and_redeploy() {
  local tunnel_url="$1"
  pushd "$FRONTEND_DIR" >/dev/null

  # Use non-sensitive vars for public tunnel urls so runtime and cli checks stay observable.
  vercel env rm BACKEND_API_BASE_URL production --yes >/dev/null 2>&1 || true
  vercel env add BACKEND_API_BASE_URL production --value "$tunnel_url" --no-sensitive --yes >/dev/null

  vercel env rm VITE_API_BASE_URL production --yes >/dev/null 2>&1 || true
  vercel env add VITE_API_BASE_URL production --value "$tunnel_url" --no-sensitive --yes >/dev/null

  vercel env rm VITE_SIGNALR_BASE_URL production --yes >/dev/null 2>&1 || true
  vercel env add VITE_SIGNALR_BASE_URL production --value "$tunnel_url" --no-sensitive --yes >/dev/null

  vercel env rm VITE_DEMO_MODE production --yes >/dev/null 2>&1 || true
  vercel env add VITE_DEMO_MODE production --value "false" --no-sensitive --yes >/dev/null

  vercel --prod --yes >/dev/null

  popd >/dev/null
}

tunnel_ok() {
  local tunnel_url="$1"
  curl -fsS -m 8 "$tunnel_url/health" >/dev/null 2>&1
}

main_loop() {
  wait_backend_up
  current_url="$(start_tunnel)"
  sync_vercel_env_and_redeploy "$current_url"
  echo "[keep-online] synced frontend to $current_url"

  while true; do
    if ! wait_backend_up; then
      sleep "$CHECK_INTERVAL_SECONDS"
      continue
    fi

    if tunnel_ok "$current_url"; then
      sleep "$CHECK_INTERVAL_SECONDS"
      continue
    fi

    echo "[keep-online] tunnel unhealthy, rotating..."
    current_url="$(start_tunnel)"
    sync_vercel_env_and_redeploy "$current_url"
    echo "[keep-online] re-synced frontend to $current_url"
    sleep "$CHECK_INTERVAL_SECONDS"
  done
}

main_loop
