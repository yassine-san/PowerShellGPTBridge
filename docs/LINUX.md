# Ubuntu desktop

Prefer the [Docker workflow](DOCKER.md): it needs nothing but Docker on the host and works on any Linux distribution.

The initial target is Ubuntu 24.04 LTS x86-64 with an unlocked GNOME desktop, Wayland or X11. Linux arm64, headless execution and root operation are outside the tested target. The official v0.0.21 client publishes both linux-amd64 and linux-arm64 assets. This project currently publishes linux-x64.

For the candidate binaries install the x64 .NET 8 runtime, python3, libwebkit2gtk-4.1-0 and libgtk-3-0t64. Extract the complete archive into a new directory owned by the desktop user. ZIP extractors may discard executable permissions:

~~~sh
chmod u+x Start-Linux.sh app-linux/Bridge app-linux/bridge-supervisor
./app-linux/Bridge --ui-test
./Start-Linux.sh "$TUNNEL_ID" "$TUNNEL_CLIENT"
~~~

Set TUNNEL_ID to your existing tunnel ID and TUNNEL_CLIENT to the full path of the official tunnel-client. Enter the restricted runtime key in the terminal when prompted. The launcher creates a temporary 0700 working directory and a private client configuration. The configuration references an environment variable. The launcher forwards only the desktop and runtime variables required by the child. See [setup](SETUP.md) for provisioning and ChatGPT steps.

For source builds, install .NET SDK 8.0.428, Node 22.14, Rust 1.94.1, build-essential, pkg-config, libwebkit2gtk-4.1-dev, libgtk-3-dev, libayatana-appindicator3-dev and librsvg2-dev. Run ./Build-Linux.sh. The PowerShell 7 SDK and bundled module manifests are published with the worker. The application does not require a separate pwsh process per command.

Linux refuses effective UID zero. A missing or inaccessible display, crashed UI, closed approval window or expired request cannot grant approval. Credentials stay in process memory for the connection. Persistent password/key-passphrase storage and retained plaintext transcripts are disabled on Linux. Metadata and host pins live under ~/.local/state/powershellgptbridge in a private directory. The bridge requires owner access and 0700 permissions. New files inherit umask 077.

The persistent worker uses private inherited streams, isolates them from its command console and creates a process group. Timeout kills the group and invalidates the session. A program that deliberately changes its group or starts another service can outlive cancellation. Remote process termination is best effort. Use Reset Session and inspect the target before another operation. The new UI is event-driven and displays completed output rather than a live terminal.

Linux build, worker and policy tests were run in an isolated Linux environment. A live Ubuntu graphical login, an actual ChatGPT tunnel request and real SSH authentication remain acceptance gates. [Test evidence](RELEASE-REVIEW.md)

## Shell

The shell follows the operating system: PowerShell on Windows, your bash or zsh on Linux (`$SHELL` when it is zsh, otherwise bash). Set `PSBRIDGE_SHELL=powershell`, `bash` or `zsh` to override; any other value stops the bridge at startup. On Linux the write-capable tool is named `execute_shell` (Windows keeps `execute_powershell`), and the fixed read-only operation is the exact command `date`, run as a fresh process so earlier commands cannot shadow it.

The bash or zsh session is persistent: variables, functions, exported environment and the working directory carry over between approved commands. It starts without rc files, stdin is empty, stdout and stderr are returned separately (64 KiB cap each), and the exit status is reported. Timeout, cancellation and Stop kill the shell's whole process group. If a command ends the shell (`exit`, `exec`, `set -e` followed by a failure) or closes stdout or stderr, the session is reported unhealthy and needs *Reset session*. Processes a command leaves running are listed in the result. The shell runs as you, so an approved command can reach more than its own output; see the trust boundary notes in [DOCKER.md](DOCKER.md#security-notes) and `Linux/ShellSession.cs`.
