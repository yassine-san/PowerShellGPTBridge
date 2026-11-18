#!/usr/bin/env bash
# Container entrypoint. Modes: start (default), ui-test, fixture, shell.
set -euo pipefail
umask 077
app=/opt/psbridge

case "${1:-start}" in
  start)
    : "${TUNNEL_ID:?TUNNEL_ID is required}"
    : "${CONTROL_PLANE_API_KEY:?CONTROL_PLANE_API_KEY is required (use ./bridgectl start)}"
    client="${TUNNEL_CLIENT:-/opt/tunnel/tunnel-client}"
    exec "$app/Start-Linux.sh" "$TUNNEL_ID" "$client"
    ;;
  ui-test) exec "$app/app-linux/Bridge" --ui-test ;;
  fixture) exec "$app/app-linux/bridge-supervisor" --fixture ;;
  shell)   exec bash -l ;;
  *) echo "unknown mode: $1 (start|ui-test|fixture|shell)" >&2; exit 2 ;;
esac
