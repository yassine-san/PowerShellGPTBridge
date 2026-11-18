#!/usr/bin/env bash
# Runs the automated suites, mirroring .github/workflows/verify.yml. Intended to run as a non-root user
# inside the Docker `test` target, but works anywhere the .NET 8 SDK is available.
#   run-tests.sh suites   unit, worker and MCP protocol suites (default)
#   run-tests.sh gui      launch the Tauri supervisor under Xvfb (needs a real `docker run`, not a build step)
set -euo pipefail
cd -- "$(dirname -- "$0")/.."
rid=unix
step() { printf '\n=== %s\n' "$*"; }

gui_smoke() {
  step "GUI smoke (both Tauri windows must become visible under Xvfb)"
  [[ -x app-linux/bridge-supervisor ]] || { echo "supervisor binary missing" >&2; return 1; }
  xvfb-run -a -s '-screen 0 1440x1000x24' bash -c '
    app-linux/bridge-supervisor --fixture >/tmp/supervisor.log 2>&1 &
    pid=$!; trap "kill $pid 2>/dev/null || true" EXIT
    for name in Operations Approval; do
      for _ in $(seq 1 40); do
        xdotool search --onlyvisible --name "PowerShell Bridge • $name" >/dev/null 2>&1 && continue 2
        sleep 1
      done
      echo "window not visible: $name" >&2; cat /tmp/supervisor.log >&2; exit 1
    done
    echo "both windows visible"'
}

if [[ ${1:-suites} == gui ]]; then gui_smoke; printf '\nGUI SMOKE PASSED\n'; exit 0; fi

step "Supervision (history, evidence, enforcement)"
dotnet run --project Tests/Supervision

step "Session (real persistent worker)"
dotnet build Tests/Session -v quiet
PSModulePath="$PWD/Tests/Session/bin/Debug/net8.0/runtimes/$rid/lib/net8.0/Modules" \
  dotnet run --no-build --project Tests/Session -- --measure

step "Core (PowerShell runspace)"
dotnet build Tests/Core -v quiet
PSModulePath="$PWD/Tests/Core/bin/Debug/net8.0/runtimes/$rid/lib/net8.0/Modules" \
  dotnet run --no-build --project Tests/Core

step "MCP protocol (real endpoint, simulated operator)"
dotnet build Tests/Protocol -v quiet
python3 Tests/Protocol/check.py
python3 Tests/Protocol/check.py dotnet clock-expiry
python3 Tests/Protocol/check.py dotnet late-approval

printf '\nALL SUITES PASSED\n'
