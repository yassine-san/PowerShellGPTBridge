# Validation

## Executed in the build environment

Linux x64, .NET SDK 8.0.419, PowerShell 7.4.17. Locked win-x64 restore and Release publish succeeded. Build.ps1 -NoRestore was also executed with PowerShell. The offline publish copied the Utility manifest into app/Modules. The entry point is a Windows x64 PE executable. Scripts parsed successfully and Backup-Bridge.ps1 produced a hash-verified copy of an extracted 0.2 release in the test environment.

| Tests | Passed | Coverage |
|---|---:|---|
| Core | 16 | Real PowerShell SDK runspace, persistent variables/functions/cwd, pinned date cmdlet, bounded streams, IPC checks |
| MCP protocol | 41 | Actual MCP SDK and Endpoint, simulated Windows UI, worker and SSH. Both protocol variants, schemas, rejection, draft feedback, task stop, busy handling and privilege modes |
| Clock and remaining budget | 6 | Two real-time MCP runs including discovery and shutdown. A real 60-second approval timer with an unresponsive simulated approver; a 46-second delayed approval with a 60-second requested execution interval |
| History and evidence | 9 | Atomic snapshots, interruption recovery, retention, secret omission, and opt-in UTF-8 added-file diffs |

Raw results are in Tests/Results. For a repeat, build Tests/Protocol then run Tests/Protocol/check.py with your dotnet executable as its first argument. Run again with clock-expiry and late-approval as separate second arguments. Run dotnet run --project Tests/Supervision for history and diff tests. On Windows that suite also exercises the actual DPAPI round trip and deletion. The Core test project needs its published SDK modules discoverable in PSModulePath when invoked as a console harness.

## Windows acceptance still pending

1. Run Test-Local.ps1 separately for Reject, close, Approve once, and expiry. No command is executed in this mode. On expiry type a partial sentence; check stderr reports approval_expired and the correct feedback length. Press Enter while entering multiline feedback and verify it does not approve.
2. Start 0.3 on your existing tunnel. On a second monitor at a different scale, focus another application and request Get-Date from the same ChatGPT conversation. Verify the popup centers on that monitor rather than the manager's monitor. Compare its outer bounds to the working-area midpoint after the entrance settles (allow one pixel for integer rounding). Repeat with the manager minimized, secondary monitor to the left, 100/150/200 percent scaling, and Windows animations off. Confirm keyboard input stays in the foreground application until you select the popup, the buttons remain usable, and long scripts can scroll, copy and expand.
3. Request the exact script $BridgeAcceptance = 83. Reject with an explanation. Request Get-Variable BridgeAcceptance -ErrorAction SilentlyContinue and approve. The rejected assignment must not have run. In a fresh request approve the assignment, then approve $BridgeAcceptance. Verify 83 returns to the same conversation. Match request IDs across conversation and Details. Each request must have a new dialog. Try the literal Get-Date and a script starting with Get-Date followed by another statement; verify only the pinned single command gets green.
4. Let an actual MCP approval expire while typing feedback. Verify the response says approval_expired, execution_attempted is false, and incomplete_feedback contains the text. Check the terminal history record. Repeat with Reject and confirm the explanation is returned as an intentional reason.
5. Request Start-Sleep -Seconds 15 with timeout_seconds=2. Verify the timer and responsive manager, terminal timeout status and worker health. Use Reset session deliberately before reusing a lost runspace. Repeat with sufficient time and press Stop task while it runs. Confirm later calls on that task cannot execute. Confirm minimize and disconnect behave differently.
6. Inspect Summary, Changes, Output and Details for several requests, filter by host and request ID, resize and restart. Metadata should survive, free-form fields should follow the retention rules. Use Keep reviewed details for a harmless field, restart and check that only the chosen fields are restored. Verify layout restores to an available display after disconnecting a monitor.
7. With your authorized test SSH host, exercise the existing connect/execute/disconnect actions. Verify the local identity and remote privilege warnings. On a first connection check the fingerprint independently, reject once and confirm feedback arrives. On a fresh approved connection, download a harmless short UTF-8 file to a new path with diff collection selected. Inspect the added lines, then repeat with a binary file and a different fresh destination. Existing destinations must remain protected. Rejection, disconnection and uncertain remote results must never trigger automatic retries.
8. Disconnect 0.3 and request another tool call. Verify that it cannot execute or display an approval on that bridge. If returning to 0.2, use the untouched installation or your locally verified backup.

The reported successful 0.2 tests are baseline evidence. Windows focus, DPI, animation, permissions, DPAPI, real SSH/SFTP and a live ChatGPT call on 0.3 have not been certified by the automated results.
