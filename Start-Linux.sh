#!/usr/bin/env bash
set -euo pipefail
umask 077
[[ $EUID != 0 && -n ${DISPLAY:-}${WAYLAND_DISPLAY:-} ]] || { echo 'Use a non-root graphical desktop session.' >&2; exit 1; }
[[ $# = 2 && $1 =~ ^tunnel_[a-zA-Z0-9_-]+$ ]] || { echo 'Usage: Start-Linux.sh TUNNEL_ID /path/to/tunnel-client' >&2; exit 2; }
client=$(realpath -- "$2")
app=$(realpath -- "$(dirname -- "$0")/app-linux/Bridge")
[[ -x "$app" && -x "$client" && ${client##*/} = tunnel-client ]] || { echo 'Missing bridge or official tunnel client.' >&2; exit 1; }
work=$(mktemp -d "${XDG_RUNTIME_DIR:-/tmp}/psbridge.XXXXXXXX")
trap 'rm -rf -- "$work"' EXIT
python3 - "$work" "$1" "$app" <<'PY'
import json,pathlib,sys
root,id,app=sys.argv[1:]
if any(ch in app for ch in '"\\\n\r'): raise SystemExit("Unsupported application path")
with open(root+"/client.json","x") as f:
    json.dump({"config_version":1,"control_plane":{"base_url":"https://api.openai.com","tunnel_id":id,"api_key":"env:CONTROL_PLANE_API_KEY"},"health":{"unix_socket":root+"/health.sock"},"admin_ui":{"open_browser":False},"mcp":{"stdio_failure_policy":"exit","commands":[{"channel":"main","command":'"'+app+'"'}]}},f)
PY
# Clear unrelated shell credentials before reading the tunnel key.
python3 - "$client" "$work/client.json" <<'PY'
import getpass,os,sys
names=("HOME","USER","LOGNAME","LANG","LC_ALL","DISPLAY","WAYLAND_DISPLAY","XAUTHORITY","DBUS_SESSION_BUS_ADDRESS","XDG_RUNTIME_DIR","XDG_DATA_DIRS","DOTNET_ROOT")
env={k:os.environ[k] for k in names if os.environ.get(k)}
env["PATH"]="/usr/local/bin:/usr/bin:/bin"
env["PSBRIDGE_TUNNEL_EXE"]=sys.argv[1]
# Non-interactive launchers (the Docker entrypoint) supply the key through the environment.
key=os.environ.get("CONTROL_PLANE_API_KEY","")
if not key:
    with open("/dev/tty","r+") as tty:
        key=getpass.getpass("Restricted Platform runtime key: ",stream=tty)
if not key:raise SystemExit("Key required")
env["CONTROL_PLANE_API_KEY"]=key
os.execve(sys.argv[1],[sys.argv[1],"run","--config",sys.argv[2]],env)
PY
