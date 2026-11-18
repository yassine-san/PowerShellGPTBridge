# Setup

## 1. Prepare the endpoints

Use the official OpenAI tunnel-client download for your OS and architecture. Verify the release digest and provenance against the release page before running it. Keep the executable named tunnel-client (tunnel-client.exe on Windows). The bridge checks the path of its parent process.

On Windows install PowerShell 7, x64 .NET 8 Desktop Runtime and WebView2 Runtime. Keep the entire extracted folder together. For a local UI-only test run `.\app-desktop\Bridge.exe --ui-test`. For the legacy WinForms source build use Build.ps1 and Test-Local.ps1. The public launcher now requires explicit -AllowElevatedGeneral for unrestricted administrator use. -AllowElevatedGetDate restricts elevated use to the pinned date operation. The application does not change UAC settings.

## 2. Provision once

Open OpenAI Platform tunnel settings for the correct organization. Create a tunnel associated with the target ChatGPT workspace. The provisioning identity needs Tunnels Read and Manage. Record the generated tunnel identifier locally. Ask the organization owner or RBAC administrator if the permission is unavailable.

In Platform API keys, create a restricted runtime key with Tunnels Read and Use. Leave unrelated permissions disabled. Do not paste the key into ChatGPT, a Git issue, a command line or an example config. The launcher reads it locally and supplies an environment reference to the official client. Rotate it if it ever appears in public Git history.

## 3. Connect

Start the corresponding launcher in an interactive desktop session. On Windows set $TunnelId to the existing identifier and $TunnelClient to the full executable path, then run:

~~~powershell
.\Start-Bridge.ps1 -Desktop -TunnelId $TunnelId -TunnelClient $TunnelClient
~~~

In ChatGPT open Plugins, choose the plus button and Add custom MCP server, select Tunnel and the same identifier. Configure the workspace-required authentication, review the permissions and create the plugin. The restricted runtime key is separate from ChatGPT app authentication. Reuse existing resources wherever possible. If the tunnel is absent, check workspace association, the operator’s Read + Use permission and account policy. Use the official client’s doctor and readiness checks for transport problems.

## 4. Validate a conversation

Request the exact literal Get-Date and reject once with a harmless explanation. Request it again and approve. Match the local ID to the response in the conversation. Request $Demo = 123 and then $Demo in two separately approved calls. Use Stop Task during a harmless sleep and check that new calls are blocked until a new task is started. Reset before reusing a lost local runspace.

For SSH, ask ChatGPT for ssh_operation with action connect, your authorized test host and port 22. Enter credentials in the local dialog only. Compare a new host key to a fingerprint obtained independently. Then request action execute with command pwd. For an SFTP download provide both an absolute new local file path and the exact remote path. Approve each step separately. Do not retry a state-changing command merely because the response timed out.

## Build host

Windows builds use the repository-pinned .NET SDK. The Tauri build additionally needs Node 22, Rust 1.94.1 MSVC x64, Visual Studio C++ Build Tools with a Windows SDK, and WebView2. Run Build-Desktop.ps1 on a build host or Windows CI runner. Copy the app-desktop folder and Start-Bridge.ps1 to the operator’s machine. Source builds use NuGet, npm and Cargo lockfiles.

Official references: [Secure MCP Tunnels](https://developers.openai.com/api/docs/guides/secure-mcp-tunnels), [tunnel-client releases](https://github.com/openai/tunnel-client/releases), [custom MCP servers in ChatGPT](https://help.openai.com/en/articles/12584461-connecting-custom-mcp-servers-in-chatgpt).
