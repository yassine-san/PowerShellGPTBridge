# PersistentPowerShellBridge 0.3

Windows x64 operator interface for the existing supervised PowerShell and SSH tools. The ZIP includes a framework-dependent build in app, complete source in Bridge, and executable tests. The Windows GUI, live SSH endpoint and ordinary ChatGPT round trip require local acceptance. See ACCEPTANCE.md.

## Side-by-side installation and rollback

1. Extract the complete ZIP into a new directory on your PC. Leave the working 0.2 directory intact.
2. In PowerShell 7, from the extracted 0.3 directory, back up the actual deployed 0.2 files. Substitute your existing directory.

~~~powershell
.\Backup-Bridge.ps1 -ExistingProject 'C:\path\to\your\existing\PersistentPowerShellBridge'
.\Test-Local.ps1
~~~

The backup script copies source, binaries and launch scripts into a fresh sibling directory, then verifies every copied file by SHA-256. Credentials and audit data under LocalAppData are outside its scope. The UI test needs an interactive Windows desktop and the x64 .NET 8 Windows Desktop Runtime. A local test never runs the command.

3. Disconnect 0.2 when ready to switch. From the 0.3 folder, use your existing tunnel ID, official tunnel client and restricted runtime key. Keep your existing ChatGPT plugin and OAuth configuration.

~~~powershell
.\Start-Bridge.ps1 -TunnelId $TunnelId -TunnelClient $TunnelClient
~~~

4. In the ordinary ChatGPT conversation, select the same plugin and request Get-Date. The tunnel readiness label reports the client's health socket. A successful tool call with a matching request ID verifies the conversation path.

To roll back, disconnect 0.3 and run Start-Bridge.ps1 from your untouched 0.2 folder or verified backup with the same arguments. Switching bridge processes ends transient PowerShell and SSH sessions. The launcher does not change UAC. The new operation-* files coexist with the existing credential and audit files. A reset is destructive to runspace variables and functions.

## Approval and supervision

The popup uses the foreground window's monitor and its working area. It accounts for the complete outer rectangle. The entrance lasts up to 210 ms and uses the Windows client-area animation setting. A topmost, independent tool window can appear while the manager is minimized. Click it or select it with Alt+Tab for keyboard interaction. Escape or Ctrl+R rejects, Ctrl+Enter approves once, and Ctrl+Shift+F12 stops the task. Closing rejects. All three actions operate on the displayed immutable request.

Green identifies the exact Get-Date route that invokes a pinned cmdlet. General scripts and SSH operations are red. Privilege is displayed independently. An elevated general-mode supervisor executes ordinary approved local commands as Administrator, including when UAC is disabled. This mirrors the reported deployed configuration. To explicitly restrict an elevated launch to Get-Date, use -AllowElevatedGetDate. Remote privilege may be unknown. A model-supplied purpose or expected effect is unverified.

Copy uses the original submitted text. Expand enlarges the command area. The countdown uses the same monotonic starting timestamp as the approval gate. Expiry prevents dispatch. A partially typed explanation is returned in incomplete_feedback when the request channel is available. A deliberate rejection uses reason. A first-use SSH host-key prompt is a second decision with its own ID. Host decisions that block authentication are returned in host_approval.

## Operation history

The manager groups information by request ID and shows the current task and session IDs. Select a history entry or search by operation title, target, task ID, request ID, status or classification. The Summary, Changes, Output and Details tabs show the corresponding record. Stop task cancels the active task and blocks new operations on it. Start new task opens a new task scope. Reset session recreates the local worker state. Minimize leaves the connection running. Disconnect terminates the bridge.

The supervisor writes one atomically replaced operation snapshot per request under the existing SID-protected %LOCALAPPDATA%\PersistentPSBridge directory. Metadata includes identity, target, classification, approval, timestamps, duration, result and truncation. Startup removes history files older than 30 days. An operation interrupted after dispatch began is displayed as result_unknown. The existing JSONL audit remains separate and has its existing operator-managed retention.

The latest 128 completed operations have full live transcripts. Original command, model explanations, paths, free-form output, error text, diffs and reasons can contain secrets. Automatic snapshots omit these free-form fields. Use **Keep reviewed details** on a completed entry to retain selected fields for later restarts. Review their complete contents first and leave any field containing a credential unchecked. Selected fields are encrypted using Windows DPAPI for the current user. Selecting no fields and confirming deletes that operation's reviewed copy. DPAPI does not protect against another process acting as that user. Credential dialogs never populate the ledger. The launcher accepts the legacy log switches; arbitrary audit command text and reasons are always hashed in 0.3.

For a successful download, the manager can verify creation of the local destination because the transfer uses exclusive CreateNew. Select the diff checkbox before approval to preview text actually written during that transfer. It is off by default. The diff is live only, limited to 8 KiB of valid UTF-8 text, and normalizes line endings for display. Larger or binary content retains the creation evidence. There is no filesystem scan, reread, or general-purpose snapshot. Other effects are labeled unverified. A timeout can leave partial files or remote side effects. Inspect the target before considering a retry.

## Timing and long-running work

The approval deadline is 60 seconds. Execution keeps its separate 1–60 second synchronous request parameter. The local request budget is 105 seconds from handling the call. A credential prompt can consume that budget. The bridge declines opening a confirmation if a full decision window cannot fit; it declines execution after approval when the requested execution interval no longer fits. Startup, IO and external cancellation can still interrupt work. SSH network handshaking uses up to 15 seconds, bounded by the requested execution timeout; the first-use human host-key check pauses that local network timer.

OpenAI documents a single 120-second deadline from hosted queue admission to final response delivery. Queue time is invisible locally and progress notifications do not renew it. The application cannot guarantee delivery of a terminal response. The local ledger and the target are the places to check an uncertain result. There is no automatic retry. Source: https://developers.openai.com/api/docs/guides/secure-mcp-tunnels

The manager shows elapsed time, target, configured timeout, and current state during a synchronous operation. The current worker and SSH implementation deliver captured streams at completion; there is no live stdout feed. Stop task is the cancellation control. Remote process termination and reversal of changes cannot be guaranteed. Multi-minute detached builds, transfers, backups, operation polling and streaming output are a separate milestone. This release starts no detached operation and changes neither MCP tool signature.

## Build and diagnostics

Run .\Build.ps1 with the .NET 8 SDK installed. Once the locked NuGet dependencies have been restored for win-x64, use .\Build.ps1 -NoRestore. Publish copies the PowerShell module directory into app\Modules. Launch the whole app directory together. Build.ps1 prints the Bridge.dll hash. The apphost alone does not identify a build.

If no approval appears, inspect launcher stderr for the executable, assembly, session, privilege and desktop check. An unhealthy local session requires a deliberate local Reset session; Stop task also requires starting a new task before execution. If the tunnel is unavailable, check the existing tunnel-client process and its readiness. If the MCP response is missing after a timeout, inspect the manager entry for the request before considering another action. Direct production Bridge.exe startup requires its tunnel parent; use --ui-test for an independent GUI check.

This candidate was built from the available 0.2 project and the reported general-mode and module-copy changes. A call to back up and compare the actual Windows installation returned session_unhealthy before execution. The remote installation was not modified or backed up by that call.
