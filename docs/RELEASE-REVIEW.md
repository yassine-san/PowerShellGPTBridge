## Review candidate, 18 November 2026
Based on public commit 0202732966a41b74efcf1f74f96eaed94d093a4f. Extract separately from the working installation. The local unpublished Windows tree could not be compared.

## Executed checks
Linux and Windows x64 framework-dependent .NET packages and Tauri supervisors compiled. Windows was cross-built. On Linux, 47 MCP endpoint checks used simulated operator and worker adapters, 16 core checks exercised PowerShell, four exercised the real persistent child process, and 16 exercised supervision. Host-key tests exercised matching and changed pins, rejection of an unknown key and interrupted trust approval through production decision code. Native headless startup and an unusable display both exited with code 1 and zero MCP stdout. Tested state directory mode 0700 and audit and lease modes 0600.

The only performance sample was the warm Linux worker over ten seconds, 0.70% of one CPU and 103.6 MiB RSS. Desktop and process-tree performance remain unmeasured.

## Exposure review
Gitleaks scanned the public Git history with redaction and reported zero findings. Pattern scanning cannot establish absence of secrets. No private machine transcripts or credential stores were used. Public Git attribution remains in upstream history.

## Security and dependency findings
The SSH trust decision now defaults to refusal and checks a stored fingerprint before accepting a connection. Linux credentials are memory-only. Upgrading the PowerShell SDK removed two previously flagged .NET transitive dependency versions. The candidate's NuGet and npm audits returned no flagged dependencies in their queried catalogs. A full Cargo.lock OSV query still flagged chrono 0.4.45 (RUSTSEC-2026-0543), glib 0.18.5 (GHSA-wrw7-89jp-8q8g, RUSTSEC-2024-0429) and proc-macro-error 1.0.4 (RUSTSEC-2024-0370). Reachability and remediation remain open. The requested .NET 8 and PowerShell 7.4 runtime family is beyond vendor support. Treat this as a review build pending dependency remediation.

## Remaining acceptance
On each target desktop, launch the isolated candidate, review the real approval window, reject Get-Date with a reason, then approve a fresh request. Compare request IDs and results in the same ChatGPT conversation. Allow another prompt to expire. Disconnect and confirm no new request runs. Test an independently verified SSH server and a changed host key using disposable accounts. Check transfers, cancellation and unknown remote outcomes. Native interactive GUI, Windows execution, live SSH/SFTP and ChatGPT tunnel tests remain pending. The isolated runner could not create the desktop sockets required for GUI tests. No native screenshots or full desktop performance measurements are claimed.
