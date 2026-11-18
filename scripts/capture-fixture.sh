#!/usr/bin/env bash
# Launches the supervisor with synthetic data under a virtual display and screenshots both windows.
# If the windows never appear it prints what is needed to find out why, then retries once with WebKit's
# sandbox disabled (a fixture UI with no untrusted content) so a restricted runner can still produce screenshots.
set -euo pipefail
# xdotool cannot match window titles when no locale is set, and ours contain a bullet.
export LANG=C.UTF-8
mkdir -p screenshots
pid=""
stop() { [[ -z $pid ]] || kill "$pid" 2>/dev/null || true; pid=""; }
trap stop EXIT

windows_visible() { xdotool search --onlyvisible --name "PowerShell Bridge" 2>/dev/null | wc -l; }

launch_and_wait() { # launch_and_wait <log> [VAR=value ...]
  local log=$1; shift
  env "$@" app-linux/bridge-supervisor --fixture >"$log" 2>&1 &
  pid=$!
  for _ in $(seq 1 25); do
    [[ $(windows_visible) -ge 2 ]] && return 0
    kill -0 "$pid" 2>/dev/null || { echo "supervisor exited early" >&2; return 1; }
    sleep 1
  done
  return 1
}

diagnose() {
  echo "::group::fixture diagnostics"
  echo "locale: LANG=$LANG LC_ALL=${LC_ALL:-unset}"
  echo "userns restriction: $(sysctl -n kernel.apparmor_restrict_unprivileged_userns 2>&1 || true) (1 blocks WebKit's bubblewrap sandbox)"
  echo "unprivileged userns clone: $(sysctl -n kernel.unprivileged_userns_clone 2>&1 || true)"
  echo "--- processes"; ps -eo pid,stat,args | grep -E 'bridge-supervisor|WebKit|Xvfb' | grep -v grep | cut -c1-160 || true
  echo "--- X windows"
  xdotool search --name '' 2>/dev/null | head -20 | while read -r w; do echo "$w: $(xdotool getwindowname "$w" 2>&1) visible=$(xdotool search --onlyvisible --name '.*' 2>/dev/null | grep -c "^$w$" || true)"; done
  echo "--- kernel denials"; sudo -n dmesg 2>/dev/null | grep -iE 'apparmor.*(DENIED|userns|bwrap)|bwrap' | tail -10 || echo "(dmesg unavailable)"
  echo "--- supervisor output ($1)"; head -40 "$1" || true
  echo "::endgroup::"
}

if ! launch_and_wait /tmp/supervisor-default.log X=1; then
  diagnose /tmp/supervisor-default.log
  stop
  echo "::warning::Fixture windows did not appear with the default WebKit sandbox; retrying with it disabled."
  if ! launch_and_wait /tmp/supervisor-nosandbox.log WEBKIT_DISABLE_SANDBOX_THIS_IS_DANGEROUS=1; then
    diagnose /tmp/supervisor-nosandbox.log
    echo "::error::Fixture windows never appeared, even without the WebKit sandbox." >&2
    exit 1
  fi
  echo "::warning::Windows appeared only with the WebKit sandbox disabled: the runner restricts unprivileged user namespaces."
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
