# PowerShellGPTBridge

**[Complete installation guide (Windows, Ubuntu, Docker)](docs/INSTALLATION.md)** — tunnel, restricted API key, plugin, prerequisites and validation.

Run an approved shell command from a ChatGPT conversation, inspect the result there, and keep the same local shell session for the next request. The shell follows the operating system: PowerShell on Windows, your bash or zsh on Linux. Each local command, SSH operation and SFTP transfer requires a fresh decision on the operator’s desktop.

**Status:** experimental. Windows 0.3 is the published WinForms baseline. Version 0.4 adds a Tauri 2 / React supervisor, a Linux backend with a native bash or zsh shell, and a Docker workflow driven by `./bridgectl`. The Linux build, shell sessions, MCP protocol and supervisor windows are covered by automated tests that run on every pull request. A live ChatGPT tunnel run, real SSH/SFTP and Windows hands-on acceptance remain outstanding. The required .NET 8 and PowerShell 7.4 runtime lines reached upstream end of support on November 10, 2026.

~~~mermaid
flowchart TD
  C["ChatGPT conversation"] <--> T["OpenAI Secure MCP Tunnel"]
  T <--> B["Local .NET MCP bridge"]
  B <--> U["Operator approves or rejects"]
  B -->|Approved local script| P["Persistent shell session<br/>PowerShell on Windows, bash or zsh on Linux"]
  B -->|Approved remote operation| S["Pinned SSH or SFTP host"]
  P --> B
  S --> B
~~~

## Start here

| Platform | Launch from the extracted candidate folder | Build and prerequisites |
|---|---|---|
| Windows x64 | In PowerShell 7, run `.\Start-Bridge.ps1 -Desktop -TunnelId $TunnelId -TunnelClient $TunnelClient` | [Setup guide](docs/SETUP.md) |
| Ubuntu 24.04 LTS x64 desktop | Run `./Start-Linux.sh "$TUNNEL_ID" "$TUNNEL_CLIENT"` | [Linux guide](docs/LINUX.md) |
| Any Linux x64 desktop with Wayland and Docker (recommended on Linux) | `./bridgectl setup --tunnel-id tunnel_…` then `./bridgectl start` | [Docker guide](docs/DOCKER.md) |

Use a standard desktop account. Install the x64 .NET 8 runtime, Windows Desktop Runtime on Windows, and the official tunnel client. The Tauri frontend uses WebView2 on Windows and WebKitGTK 4.1 on Linux. An extracted binary distribution needs no Rust, Node or Visual C++ compiler on the operator’s laptop. A source checkout requires a separate build step.

In Platform, create a Secure MCP Tunnel associated with the intended ChatGPT workspace. Use an organization role with **Tunnels Read + Manage** for provisioning. Give the restricted runtime key **Tunnels Read + Use**. Enter that key locally in the launcher. In ChatGPT, choose **Plugins → Add custom MCP server → Connection → Tunnel**, select the tunnel, configure the authentication requested for the workspace, and enable the plugin in the conversation. Existing users can reuse their tunnel and plugin. Write-capable tool availability depends on account and workspace policy. [Detailed setup and troubleshooting](docs/SETUP.md)

Ask ChatGPT to **run the exact command Get-Date with the bridge** (`date` on Linux). Inspect the request ID, machine and identity, then choose Approve once. Match the result to the local history entry. Rejecting with a reason returns feedback without dispatch.

## Capabilities

- Persistent shell variables, functions and working directory (PowerShell on Windows, bash or zsh on Linux; `PSBRIDGE_SHELL` overrides the choice). Reset discards session state.
- Two write-capable MCP tools: `execute_powershell` (Windows) or `execute_shell` (Linux), and `ssh_operation`. The local READ indicator is reserved for the pinned date operation: the `Get-Date` cmdlet on Windows, the `date` command on Linux.
- SSH connection, execution, explicit remote sudo, disconnect and no-overwrite transfers. Connect first, inspect first-use host fingerprints independently, and reject unexpected changes.
- A 60-second approval deadline and separate bounded synchronous execution timeouts. Stop Task blocks subsequent calls for the stopped task. A remote timeout can leave changes behind.
- Searchable operation history, decision feedback, output, errors and observed change evidence. A successful SFTP download can produce an optional bounded diff of the newly created file. Arbitrary commands have unverified side effects.
- Tauri communicates with the backend through private inherited process streams. The MCP stream is separate. No new TCP control listener.

## Review the interface

Run the bundled `bridge-supervisor --fixture` (Windows: `app-desktop\bridge-supervisor.exe --fixture`; Linux: `app-linux/bridge-supervisor --fixture`). This opens both real windows with labeled synthetic data and no execution backend. On Linux with Docker, `./bridgectl fixture` does the same. The CI workflow captures the native Linux windows in a disposable desktop on every pull request and attaches the PNGs as the `native-linux-fixture-screenshots` artifact.

## Development and release

Windows compatibility build: `.\Build.ps1`. Windows Tauri build on a provisioned build host: `.\Build-Desktop.ps1`. Ubuntu Tauri build: `./Build-Linux.sh`. Docker (any Linux distribution, nothing installed on the host): `./bridgectl build`, and `./bridgectl test` to run every suite in a clean container. Build into a new directory and retain the existing installation for rollback. To revert, disconnect the candidate and invoke the old launcher. Each process change ends transient sessions.

[Security model and reporting](SECURITY.md) · [Audit and test evidence](docs/RELEASE-REVIEW.md) · [Contributing](CONTRIBUTING.md) · [MIT license](LICENSE)

Next release gates are native desktop/tunnel acceptance on both platforms, a real SSH/SFTP round trip, full process-tree performance measurements and migration to a supported .NET/PowerShell runtime pair. Detached jobs are outside this release.
