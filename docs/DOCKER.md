# Docker workflow (Linux)

Build, test and run the Linux backend without installing .NET, Rust, Node or WebKit on the host. Everything lives in two images and two named volumes. `./bridgectl` wraps every step.

## Requirements

- Linux x86_64 desktop with a **Wayland** session, and Docker (your user in the `docker` group).
- `curl`, `sha256sum` and `python3` on the host, only to download and verify the tunnel client.
- An OpenAI Platform tunnel and a restricted runtime key (Tunnels Read + Use). See [SETUP.md](SETUP.md).

## Quick start

~~~sh
./bridgectl doctor                       # check the host
./bridgectl setup --tunnel-id tunnel_…   # build the image, install the pinned tunnel-client
./bridgectl test                         # optional: run every suite in a clean container
./bridgectl fixture                      # optional: look at both windows with synthetic data
./bridgectl start                        # prompts for the runtime key (hidden input)
./bridgectl status
./bridgectl stop
~~~

Then connect the tunnel in ChatGPT as described in [SETUP.md](SETUP.md) and ask it to run `date` with the bridge.

## Commands

| Command | What it does |
|---|---|
| `setup [--tunnel-id ID]` | Checks the host, builds the image, installs the pinned tunnel-client into a volume |
| `build` | Rebuilds the runtime image |
| `test` | Builds the `test` target without cache, then launches the GUI under Xvfb in a real container run |
| `start [--tunnel-id ID]` | Starts the container, delivers the key, waits for readiness, detects a rejected key |
| `stop`, `restart` | Graceful stop (15 s) and removal |
| `status` | Container state, processes and the tunnel-client health endpoints |
| `logs [-f]` | Container logs |
| `ui-test`, `fixture` | Approval window test, synthetic-data UI preview |
| `shell` | Shell in the running container (or a fresh one) |
| `doctor` | Diagnoses the host |
| `clean [--all] [--yes]` | Removes images. `--all` also deletes volumes (host-key pins, audit history) and the saved tunnel id, after confirmation |
| `os` | Prints the detected OS. `bridgectl` only takes the Docker path on Linux and points Windows users at `Start-Bridge.ps1` |

## What runs where

Approved commands run **inside the container**, in its own filesystem, as an unprivileged user. They do not touch your host files. SSH host-key pins and the audit log live in the `psbridge-state` volume and survive restarts.

## Security notes

- **The container shares your display.** That is how the approval window reaches you, but anything that shares a display can synthesize input, so a command you approved once could in principle click "Approve" on later requests. `bridgectl` therefore uses Wayland only. X11 has no client isolation at all and is refused unless you set `PSBRIDGE_ALLOW_X11=1`. Read every request before approving. Running the approval UI outside the sandbox that executes commands is the proper fix and is not done yet.
- **Same-user trust boundary.** The shell runs as the container user, the same account as the bridge. An approved command can read the bridge's `/proc` entries and can leave background processes that interfere with later requests (they are reported after every request, and *Reset session* ends them). A process that calls `setsid` leaves the tracked process group.
- **The runtime key** is piped over `docker exec` into a private tmpfs file that is read once and deleted. It never appears in `docker inspect`, on a command line or in an image layer. It is still present in the environment of the official tunnel-client process, as that client requires.
- **Hardening flags:** read-only root filesystem, all capabilities dropped, `no-new-privileges`, a PID limit, a 2 GiB memory limit (`PSBRIDGE_MEMORY`), tmpfs for writable paths. Network egress is not restricted.
- `bridgectl` only stops, restarts or cleans containers and volumes it created itself (labelled `psbridge.managed=1`).
- The tunnel-client version and its SHA-256 are pinned in `bridgectl`. Another version needs `--client-version` and `--client-sha256`.

## Tiling window managers (Hyprland, sway, i3)

Both windows are created undecorated and the approval window asks to be always on top, which a Wayland client cannot enforce. Under a tiling compositor they open tiled and may not take focus, so you can miss an approval while on another workspace (the request expires after 60 seconds). Add a rule that floats and centers them and makes them visible on all workspaces. Match on:

- application id / class `bridge-supervisor`
- titles `PowerShell Bridge • Approval` and `PowerShell Bridge • Operations`

The rule syntax differs between compositor versions, so check your compositor's documentation. For Hyprland the rule should float the window, center it and pin it; this repository has not verified an exact line for every version.

## Troubleshooting

- **`doctor` says only X11 was found.** See the security note above, or use a Wayland session.
- **Window titles do not match in tools such as `xdotool`.** Set a UTF-8 locale; the images already do (`LANG=C.UTF-8`).
- **`Build-Linux.sh` fails on your distribution with "Install the [8.0.428] .NET SDK".** `global.json` pins an exact SDK feature band. The Docker image installs it, which is one reason to build there.
- **`start` reports a 401.** The key lacks Tunnels Read + Use, or belongs to a different organization than the tunnel.
