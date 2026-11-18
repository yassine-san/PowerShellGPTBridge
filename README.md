# PowerShellGPTBridge

**Human-supervised PowerShell and SSH execution from ChatGPT.**

PowerShellGPTBridge connects ChatGPT to a Windows computer through an MCP bridge and OpenAI Secure MCP Tunnel. ChatGPT can request PowerShell commands and SSH/SFTP operations; a local approval window shows the exact operation before execution.

> **Status:** Experimental | **Published source:** v0.3 | **Platform:** Windows x64 | **License:** MIT
>
> The published v0.3 interface uses WinForms. The Tauri/React redesign is planned, not released.

## How it works

```text
ChatGPT conversation
        |
        v
OpenAI Secure MCP Tunnel
        |
        v
PowerShellGPTBridge (.NET 8, Windows)
        |
        +-- Local approval: Approve once / Reject / Stop task
        |                                      |
        |                                   Reject? Stop.
        v
Approved operation only
        |
        +-- Persistent local PowerShell 7
        +-- Remote SSH / SFTP
        |
        v
Execution result + operation history
        |
        v
ChatGPT
```

The bridge enforces approval. The AI's explanation is not authorization, and approval of one command never authorizes another.

## Current capabilities (v0.3)

- **Mandatory local approval:** Preview the exact command and target, approve once, reject with feedback, or stop the task. The approval deadline is **60 seconds**; expiration does not execute the operation.
- **Persistent PowerShell session:** Variables and functions can survive separate approved calls.
- **SSH/SFTP:** Connect to remote servers, execute commands, and transfer files with supervised decisions. SSH credentials remain local.
- **Operation history:** Search by request, status or target, inspect available output and timing, and retain specifically reviewed transcript fields using Windows user-scoped encryption.
- **Limited verified changes:** A successful SFTP download can verify local file creation and optionally provide a small text diff. General commands do **not** receive comprehensive side-effect verification.
- **Session controls:** Stop Task, Start New Task, Reset Session, and Disconnect.

**Classification:** The pinned `Get-Date` operation is READ (green); general PowerShell scripts and SSH operations default to WRITE (red). Classification and privilege level are separate.

## Installation and local test

This repository contains **source code**, not the compiled `app/` binaries. Requirements:

- Windows x64 with an interactive desktop
- .NET 8 SDK for building and .NET 8 Windows Desktop Runtime for the framework-dependent application
- PowerShell 7
- OpenAI Secure MCP Tunnel client and a configured MCP connection for live ChatGPT operation

From PowerShell 7 in the project root:

```powershell
.\Build.ps1
.\Test-Local.ps1
```

The UI test opens a simulated approval request without executing its sample command. The build writes the Windows application to `app/`. If script execution is blocked, review the scripts before applying a process-scoped execution-policy override.

Once the tunnel and plugin are configured, the existing launcher uses:

```powershell
.\Start-Bridge.ps1 -TunnelId $TunnelId -TunnelClient $TunnelClient
```

Keep tunnel secrets and credentials out of source control. For full deployment and rollback instructions, see the [v0.3 technical guide](docs/README-v0.3.md) and [acceptance procedure](ACCEPTANCE.md).

## Security model and limitations

**This application can execute arbitrary commands and must be treated as a privileged automation interface.**

- Every operation requires explicit local approval, tied to the displayed command and target.
- Examine the complete command, host, execution identity, privilege level, and expected effects. AI-provided impact descriptions are **unverified predictions**.
- If launched with administrator privileges, approved general commands may also execute elevated. UAC-disabled environments warrant extra caution.
- Never commit SSH private keys, passwords, tunnel configuration, audit transcripts, or operation-history files.
- Execution timeouts and connection failures can leave effects behind. Check the target before retrying.
- v0.3 has synchronous execution (up to 60 seconds per operation), no detached long-running jobs or live stdout streaming, and no general filesystem change tracking.
- Windows GUI and live SSH/ChatGPT behavior need acceptance testing on the deployment machine.

## Repository structure

```text
Bridge/               .NET/C# execution, approval, SSH, manager source
Tests/                Core, protocol, and supervision test sources
docs/README-v0.3.md   Original detailed operational README
ACCEPTANCE.md         Windows GUI and MCP acceptance checklist
Build.ps1             Build/publish
Start-Bridge.ps1      Launcher
Test-Local.ps1        UI-only test
hosts.example.json    Placeholder SSH host metadata
LICENSE               MIT
```

## Roadmap (not yet released)

- Tauri 2 / React / TypeScript visual redesign with performance measurements
- Better verified change evidence and diffs
- Supervised long-running jobs with status and cancellation
- Possible Linux support after platform-specific approval and execution work

The existing WinForms manager and approval popup do **not** represent the proposed final visual design. Screenshots have not yet been published.

## Development and licensing

Read [DEPENDENCIES.md](DEPENDENCIES.md) and [THIRD-PARTY.txt](THIRD-PARTY.txt) for third-party notices. Original project code is available under the [MIT License](LICENSE); third-party components retain their own terms.

Pull requests and issues are welcome. Do not post credentials or sensitive command output in public bug reports.

**Independent project — not an official OpenAI application.**
