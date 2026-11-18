#!/usr/bin/env bash
set -euo pipefail
# xdotool cannot match window titles (even ASCII ones) when no locale is set, and ours contain a bullet.
export LANG=C.UTF-8
mkdir -p screenshots
app-linux/bridge-supervisor --fixture &
pid=$!
trap 'kill "$pid" 2>/dev/null || true' EXIT
for name in Approval Operations; do
  window=$(timeout 25 xdotool search --sync --onlyvisible --name "PowerShell Bridge • $name" | head -1)
  xdotool windowraise "$window"
  sleep 1
  import -window "$window" "screenshots/$name.png"
done
