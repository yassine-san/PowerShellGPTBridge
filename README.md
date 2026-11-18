# PowerShellGPTBridge

Run an approved PowerShell command from a ChatGPT conversation, inspect the result there, and keep the same local PowerShell session for the next request. Each local command, SSH operation and SFTP transfer requires a fresh decision on the operator’s desktop.

**Status:** experimental. Windows 0.3 is the published WinForms baseline. This review candidate adds a Tauri 2 / React supervisor and an Ubuntu x64 backend. Live desktop and tunnel acceptance remains outstanding. The required .NET 8 and PowerShell 7.4 runtime lines reached upstream end of support on November 10, 2026.

~~~mermaid
flowchart TD
  C["ChatGPT conversation"] <--> T["OpenAI Secure MCP Tunnel"]
  T <--> B["Local .NET MCP bridge"]
  B <--> U["Operator approves or rejects"]
  B -->|Approved local script| P["Persistent PowerShell worker"]
  B -->|Approved remote operation| S["Pinned SSH or SFTP host"]
  P --> B
  S --> B
~~~

## Start here

| Platform | Launch from the extracted candidate folder | Build and prerequisites |
|---|---|---|
| Windows x64 | In PowerShell 7, run `.\Start-Bridge.ps1 -Desktop -TunnelId $TunnelId -TunnelClient $TunnelClient` | [Setup guide](docs/SETUP.md) |
| Ubuntu 24.04 LTS x64 desktop | Run `./Start-Linux.sh "$TUNNEL_ID" "$TUNNEL_CLIENT"` | [Linux guide](docs/LINUX.md) |

Use a standard desktop account. Install the x64 .NET 8 runtime, Windows Desktop Runtime on Windows, and the official tunnel client. The Tauri frontend uses WebView2 on Windows and WebKitGTK 4.1 on Linux. An extracted binary distribution needs no Rust, Node or Visual C++ compiler on the operator’s laptop. A source checkout requires a separate build step.

In Platform, create a Secure MCP Tunnel associated with the intended ChatGPT workspace. Use an organization role with **Tunnels Read + Manage** for provisioning. Give the restricted runtime key **Tunnels Read + Use**. Enter that key locally in the launcher. In ChatGPT, choose **Plugins → Add custom MCP server → Connection → Tunnel**, select the tunnel, configure the authentication requested for the workspace, and enable the plugin in the conversation. Existing users can reuse their tunnel and plugin. Write-capable tool availability depends on account and workspace policy. [Detailed setup and troubleshooting](docs/SETUP.md)

Ask ChatGPT to **run the exact command Get-Date with the bridge**. Inspect the request ID, machine and identity, then choose Approve once. Match the result to the local history entry. Rejecting with a reason returns feedback without dispatch.

## Capabilities

- Persistent PowerShell variables, functions and working directory. Reset discards session state.
- Two write-capable MCP tools, `execute_powershell` and `ssh_operation`. The local READ indicator is reserved for the pinned date cmdlet.
- SSH connection, execution, explicit remote sudo, disconnect and no-overwrite transfers. Connect first, inspect first-use host fingerprints independently, and reject unexpected changes.
- A 60-second approval deadline and separate bounded synchronous execution timeouts. Stop Task blocks subsequent calls for the stopped task. A remote timeout can leave changes behind.
- Searchable operation history, decision feedback, output, errors and observed change evidence. A successful SFTP download can produce an optional bounded diff of the newly created file. Arbitrary commands have unverified side effects.
- Tauri communicates with the backend through private inherited process streams. The MCP stream is separate. No new TCP control listener.

## Review the interface

Run the bundled `bridge-supervisor --fixture` (Windows: `app-desktop\bridge-supervisor.exe --fixture`; Linux: `app-linux/bridge-supervisor --fixture`). This opens both real windows with labeled synthetic data and no execution backend. The CI workflow captures these native Linux windows in a disposable desktop and attaches the PNGs as artifacts after a successful run. Native screenshots are pending for this candidate.

## Development and release

Windows compatibility build: `.\Build.ps1`. Windows Tauri build on a provisioned build host: `.\Build-Desktop.ps1`. Ubuntu Tauri build: `./Build-Linux.sh`. Build into a new directory and retain the existing installation for rollback. To revert, disconnect the candidate and invoke the old launcher. Each process change ends transient sessions.

[Security model and reporting](SECURITY.md) · [Audit and test evidence](docs/RELEASE-REVIEW.md) · [Contributing](CONTRIBUTING.md) · [MIT license](LICENSE)

Next release gates are native desktop/tunnel acceptance on both platforms, a real SSH/SFTP round trip, full process-tree performance measurements and migration to a supported .NET/PowerShell runtime pair. Detached jobs are outside this release.
