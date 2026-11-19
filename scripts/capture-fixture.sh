#!/usr/bin/env bash
# Launches the supervisor with synthetic data under a virtual display and screenshots both windows.
set -euo pipefail
# xdotool cannot match window titles when no locale is set, and ours contain a bullet.
export LANG=C.UTF-8
# GTK makes a synchronous D-Bus call at startup. On CI runners a user bus socket exists under
# /run/user/<uid> but nothing answers it, so the call blocks for the 25 s D-Bus timeout and the
# windows appear too late. The fixture needs no session services: point it at a dead bus so the
# call fails immediately.
export DBUS_SESSION_BUS_ADDRESS=unix:path=/nonexistent
mkdir -p screenshots
app-linux/bridge-supervisor --fixture >/tmp/supervisor.log 2>&1 &
pid=$!
trap 'kill "$pid" 2>/dev/null || true' EXIT

windows_visible() { xdotool search --onlyvisible --name "PowerShell Bridge" 2>/dev/null | wc -l; }

for _ in $(seq 1 60); do
  [[ $(windows_visible) -ge 2 ]] && break
  kill -0 "$pid" 2>/dev/null || { echo "::error::supervisor exited early"; cat /tmp/supervisor.log; exit 1; }
  sleep 1
done
if [[ $(windows_visible) -lt 2 ]]; then
  echo "::group::fixture diagnostics"
  ps -eo pid,stat,args | grep -E 'bridge-supervisor|WebKit|Xvfb' | grep -v grep | cut -c1-160 || true
  xdotool search --name '' 2>/dev/null | while read -r w; do echo "$w: $(xdotool getwindowname "$w" 2>&1)"; done
  cat /tmp/supervisor.log
  echo "::endgroup::"
  echo "::error::Fixture windows did not appear within 60 s"
  exit 1
fi

# A mapped window can still be unpainted (solid background) for a while, so capture until it has content.
painted() { [[ $(convert "$1" -format '%[fx:standard_deviation > 0.01 ? 1 : 0]' info: 2>/dev/null) == 1 ]]; }

for name in Approval Operations; do
  window=$(timeout 25 xdotool search --sync --onlyvisible --name "PowerShell Bridge • $name" | head -1)
  xdotool windowraise "$window"
  for _ in $(seq 1 30); do
    sleep 0.5
    import -window "$window" "screenshots/$name.png"
    painted "screenshots/$name.png" && break
  done
  painted "screenshots/$name.png" || echo "::warning::$name window still looks blank after 15 s"
done
