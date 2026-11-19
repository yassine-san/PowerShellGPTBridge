#!/usr/bin/env bash
# Container entrypoint. Modes: start (default), ui-test, fixture, shell.
set -euo pipefail
umask 077
app=/opt/psbridge

case "${1:-start}" in
  start)
    : "${TUNNEL_ID:?TUNNEL_ID is required}"
    : "${PSBRIDGE_KEY_FILE:?PSBRIDGE_KEY_FILE is required (use ./bridgectl start)}"
    client="${TUNNEL_CLIENT:-/opt/tunnel/tunnel-client}"
    # bridgectl delivers the runtime key into this private tmpfs file; Start-Linux.sh reads and deletes it.
    for _ in $(seq 1 120); do [[ -s $PSBRIDGE_KEY_FILE ]] && break; sleep 0.5; done
    [[ -s $PSBRIDGE_KEY_FILE ]] || { echo "runtime key was not delivered" >&2; exit 1; }
    exec "$app/Start-Linux.sh" "$TUNNEL_ID" "$client"
    ;;
  ui-test) exec "$app/app-linux/Bridge" --ui-test ;;
  fixture) exec "$app/app-linux/bridge-supervisor" --fixture ;;
  shell)   exec bash -l ;;
  *) echo "unknown mode: $1 (start|ui-test|fixture|shell)" >&2; exit 2 ;;
esac
