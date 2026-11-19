# Linux port: what was done

Work done between 18 and 19 November 2026 to make the bridge usable on Linux, run it in Docker, give Linux a native shell, and get CI green. Every change went through its own branch and pull request.

## Pull requests

| PR | Change | Result |
|---|---|---|
| #2 | Docker environment and the `bridgectl` manager script | merged |
| #3 | CI fix: the Linux screenshot step | merged |
| #4, #5 | Native bash and zsh backend with OS detection, and its docs | merged into their stack branches by mistake, relanded by #6 |
| #6 | Lands #4 and #5 on `main` | merged |
| #7 | README and installation guide brought up to date | merged |
| #8 | CI runs on pull requests only, not again on merge | merged |
| #9 | Rebuild a stale image, optional saved runtime key | open |

## 1. Docker environment (#2)

- `Dockerfile` on Ubuntu 24.04, multi-stage: toolchain, build, test, runtime. The .NET SDK matches `global.json` exactly, Node is checksum-verified, Rust is pinned.
- `docker/run-tests.sh`: the same suites as CI, run as a non-root user. `docker/entrypoint.sh`: container modes `start`, `ui-test`, `fixture`, `shell`.
- `bridgectl` commands: `setup`, `build`, `test`, `start`, `stop`, `restart`, `status`, `logs`, `shell`, `ui-test`, `fixture`, `doctor`, `clean`, `os`. It detects the OS and only takes the Docker path on Linux.
- The official tunnel-client is downloaded into a volume and checked against a SHA-256 pinned in the script.
- `Start-Linux.sh` can read the runtime key from an explicit `PSBRIDGE_KEY_FILE` (a detached container has no terminal).

Hardening added after an independent review:

- The runtime key is piped over `docker exec` into a private tmpfs file that is read once and deleted. It never appears in `docker inspect`, on a command line or in an image layer.
- Wayland only. X11 needs `PSBRIDGE_ALLOW_X11=1`, because anything sharing the display can inject input into the approval window.
- Read-only root filesystem, all capabilities dropped, `no-new-privileges`, PID and memory limits, tmpfs for writable paths.
- `bridgectl` only stops or removes containers and volumes it labelled itself. `clean --all` asks first.
- `start` waits for the tunnel client's `/readyz` and detects a rejected key (401).

## 2. CI fix (#3)

The Linux job had failed on every run, only at the screenshot step. Three separate causes were found:

1. With no locale, `xdotool` cannot match window titles (reproduced in a container; not the cause on the runner).
2. **The real cause on the runner:** GTK makes a synchronous D-Bus call at startup. On the runner a user bus socket exists but nothing answers it, so the call blocks for the 25 s D-Bus timeout. The windows appeared after about 37 s and the script gave up at 25 s. Measured: default 37 s, dead bus address 2 s. WebKit sandbox, compositing and GL settings made no difference.
3. The approval screenshot was a blank 236-byte image even in passing runs; the capture now waits until each window has painted.

The fix points the fixture at a dead D-Bus address, waits up to 60 s and waits for painted windows.

## 3. Native shell backend (#4 to #6)

- The shell follows the OS: PowerShell on Windows, your bash or zsh on Linux. `PSBRIDGE_SHELL=powershell|bash|zsh` overrides it; other values are refused.
- Linux tool is `execute_shell`; the fixed read operation is the exact command `date`, run as a fresh process so earlier commands cannot shadow it. The Windows tool name, description and behaviour are unchanged.
- One persistent shell per session: variables, functions, exported environment and the working directory persist. Each approved command is written verbatim to a private mode 600 file and sourced with stdin from `/dev/null`; completion is a random per-request marker. The shell runs in its own process group, and timeout, cancel and Stop kill the group.
- Output is capped at 64 KiB per stream without splitting surrogate pairs, requests are serialised, rc files are not read, the tunnel key and `PSBRIDGE_*` settings are removed from the shell environment.
- Findings from a security review that were fixed: the working directory is read from `/proc` instead of the spoofable `$PWD`; closing stdout or stderr ends the session instead of hiding errors; processes a command leaves running are reported after every request; zombie reaping; executable resolution refuses world-writable files.
- Tests: `Tests/Shell` has 6 detection cases and 23 session cases each for bash and zsh. The MCP protocol check also runs the real endpoint with the bash profile (non-Windows hosts only).

## 4. Docs and CI trigger (#7, #8)

- README, `docs/INSTALLATION.md`, `docs/SETUP.md`, `docs/LINUX.md` and the new `docs/DOCKER.md` describe the Docker route, the native shell, the `execute_shell` tool and the `date` test.
- CI runs once per pull request (plus manual start), with a concurrency group that cancels a superseded run. It no longer runs on every push and again on merge.

## 5. First live run and what it exposed (#9)

The first live ChatGPT test worked, but the tool showed `Get-Date`: the image had been built before the shell backend merged, and `bridgectl setup` never rebuilt an existing image. #9 labels the image with a fingerprint of the sources and rebuilds when it is stale, adds an optional saved runtime key (`bridgectl key save`, mode 600, off by default), and documents that closing the Operations window disconnects the bridge.

## What was proven, and what was not

Proven by automated runs: every suite passes in the container and in CI on both platforms; the supervisor windows render under Xvfb and as native Wayland windows on Hyprland; the real tunnel-client launches the bridge (parent-process check passes) and OpenAI answers a fake key with 401, which `bridgectl start` reports; the first live ChatGPT request through the tunnel worked.

Not yet verified:

- The native bash/zsh tool through ChatGPT on the rebuilt image.
- Real SSH/SFTP, and Windows hands-on acceptance of the shared changes (CI only builds and tests Windows).
- zsh in GitHub CI (the runner has no zsh; those tests run in `./bridgectl test`).
- The Hyprland window rule: the approval window opens tiled and unfocused without one.

## Known limits

- The container shares your display, so a command you approved once could in principle click Approve on later requests. Running the approval window outside the sandbox that executes commands is the proper fix and is not done.
- The shell runs as the same user as the bridge. An approved command can reach the bridge's `/proc` entries and can leave processes that interfere later; a process that calls `setsid` leaves the tracked group. A PID namespace would be needed, and Docker's default seccomp profile blocks it.
- The tunnel client keeps the key in its own process environment, as it requires.
- .NET 8 and PowerShell 7.4 passed end of support on 10 November 2026; PowerShell 7.6 LTS is available.
- `dotnet-install.sh` and `rustup` are downloaded without a pinned hash in the Dockerfile.

## Useful commands

~~~sh
./bridgectl doctor              # host check, image freshness, saved key
./bridgectl setup --tunnel-id tunnel_...
./bridgectl start               # asks for the key unless saved
./bridgectl status              # container, processes, tunnel health
./bridgectl logs -f
./bridgectl stop
./bridgectl test                # every suite in a clean container
./bridgectl key save|forget|status
~~~
