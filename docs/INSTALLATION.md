# PowerShellGPTBridge v0.4 — Complete installation guide

**Windows x64 · Ubuntu 24.04 x64 · OpenAI Secure MCP Tunnel · Restricted API key · ChatGPT plugin**

> **Status:** v0.4 source is on main; Linux desktop GUI/tunnel acceptance remains incomplete. An official fully verified v0.4 release has not been published.

> Example install paths are not hardcoded. Keep the client executable named tunnel-client.exe on Windows or tunnel-client on Linux.

v0.4 source code has been merged into main. No official v0.4 GitHub Release was published when this guide was prepared. A source checkout is NOT the same as a ready-to-run binary package: you must have a built distribution (Windows app-desktop/ or Linux app-linux/) or build it yourself. Linux build and core tests passed in CI, but live Linux GUI/tunnel acceptance remains unverified; the automated screenshot step timed out. Do not describe Linux as fully verified yet.

This guide follows the repository launchers and docs as of 2026-11-18. OpenAI Platform/ChatGPT labels and availability may change depending on the account or workspace. A platform API key and the ChatGPT plugin's authentication are separate settings.


## OVERVIEW

ChatGPT -> custom MCP plugin -> OpenAI Secure MCP Tunnel -> official tunnel-client on your machine -> PowerShellGPTBridge -> local human approval -> persistent PowerShell session.

Each command requires a fresh local approval. The bridge does not require exposing a public inbound port.


## CREATE TUNNEL AND RESTRICTED API KEY (BOTH PLATFORMS)



### STEP 1 — Check that the Secure MCP Tunnel feature is available

1. Visit https://platform.openai.com/
2. Sign in with the intended OpenAI account.
3. Select the correct Platform organization and intended ChatGPT workspace.
4. Find the Secure MCP Tunnels area under organization settings.
5. The provisioning identity requires Tunnels Read + Manage. If the menu/permission is unavailable, ask the organization/workspace administrator. Do not assume every account can provision tunnels.
  Documentation: https://developers.openai.com/api/docs/guides/secure-mcp-tunnels


### STEP 2 — Create a tunnel

1. Choose Create tunnel (or the equivalent action in Platform).
2. Associate it with the ChatGPT workspace that will use the plugin.
3. Copy the generated identifier; it begins with tunnel_.
4. Save the identifier locally, e.g., tunnel_YOUR_REAL_ID.
5. If a working tunnel already exists for this machine/workspace, you may reuse it rather than creating another.
  NOTE: A tunnel ID is not an API key.


### STEP 3 — Create a restricted runtime API key

1. Visit https://platform.openai.com/api-keys
2. Choose Create new secret key.
3. Give it a descriptive name, e.g. PowerShellGPTBridge-runtime.
4. Choose Restricted permissions.
5. Give Tunnels the minimum required Read + Use permission; disable unrelated API permissions.
6. Create the key and securely save it, because you may only be shown it once.
7. Never paste this key into ChatGPT, a GitHub issue, a README, a shell command, or a committed config file.
8. The bridge launcher prompts locally for the key at startup.
9. If the key is compromised, revoke/rotate it in Platform.
  NOTE: Creating/provisioning a tunnel and operating one may require different permissions/identities.


### STEP 4 — Download the official tunnel client for YOUR OS

  Official releases: https://github.com/openai/tunnel-client/releases
1. Choose an official release suitable for your architecture.
2. Windows x64: extract the Windows executable and keep its name tunnel-client.exe.
3. Ubuntu x64: extract the Linux executable and keep its name tunnel-client (no .exe).
4. Verify release provenance and the published digest/checksum where available.
5. Save the executable wherever you prefer. The directory is NOT hardcoded.
6. Write down its absolute path; the launchers use this user-supplied path.


## WINDOWS 10/11 x64



### STEP 5W — Install prerequisites

1. Install PowerShell 7 (open pwsh, not legacy powershell.exe).
2. Install the x64 .NET 8 Desktop Runtime compatible with the current build.
3. Install/confirm Microsoft Edge WebView2 Runtime.
4. Obtain a BUILT v0.4 distribution with Start-Bridge.ps1 and app-desktop/ together.
     A GitHub source ZIP alone will not contain the compiled app-desktop/ unless explicitly packaged.
5. If building from source instead, follow the Windows build requirements in the repository's docs/SETUP.md (requires .NET SDK, Rust, Node, C++ build tools, etc.).
6. Use a normal (non-Administrator) interactive desktop session by default.


### STEP 6W — Place the tunnel client and check its path

  Example location (NOT REQUIRED): C:\Tools\OpenAI\tunnel-client.exe
  In pwsh:
    $TunnelClient = 'C:\Tools\OpenAI\tunnel-client.exe'
    Test-Path -LiteralPath $TunnelClient
  Expect: True
  Or replace it with your real absolute location, such as D:\Apps\tunnel-client.exe.
  Only the executable filename tunnel-client.exe is required by the Windows launcher.


### STEP 7W — Enter the v0.4 distribution folder

  Example:
    Set-Location 'C:\Path\To\PowerShellGPTBridge-v0.4'
    Test-Path '.\Start-Bridge.ps1'
    Test-Path '.\app-desktop\Bridge.exe'
  Both checks must return True.
  Optional LOCAL UI test without a tunnel:
    .\app-desktop\Bridge.exe --ui-test


### STEP 8W — Set tunnel ID, start bridge and enter secret

  In PowerShell 7:
    $TunnelId = Read-Host 'Tunnel ID'
    $TunnelClient = 'C:\Path\To\tunnel-client.exe'
    .\Start-Bridge.ps1 -Desktop -TunnelId $TunnelId -TunnelClient $TunnelClient
  Replace the client path and provide your actual tunnel_... ID.
  When prompted for "Restricted Platform runtime key (Tunnels Read + Use)", paste the secret in the LOCAL prompt (secure entry, not on the command line).
  Leave the terminal open while using the bridge.

  IMPORTANT ABOUT ADMINISTRATOR MODE:
  If your Windows PowerShell session is elevated, the launcher intentionally refuses general execution without explicit opt-in. Prefer a standard user session. If you explicitly require admin-level command execution and understand the risk, add -AllowElevatedGeneral. Do not enable this by default just to make setup easier.


### STEP 9W — Confirm the local UI starts

  The desktop approval/operations UI should become available. If it does not, inspect the launcher error and prerequisites. Avoid killing an existing working bridge instance; only one instance for the same identity may be allowed.


## LINUX WITH DOCKER (RECOMMENDED ON LINUX)

On any Linux x86-64 desktop with a Wayland session and Docker, skip the Ubuntu steps below: nothing but Docker is installed on the host, and `./bridgectl` builds the image, downloads and checksum-verifies the tunnel client, asks for the runtime key at a hidden prompt and manages the container.

    ./bridgectl doctor
    ./bridgectl setup --tunnel-id tunnel_...
    ./bridgectl start

See [docs/DOCKER.md](DOCKER.md) for the commands, what runs where and the security notes (approved commands run inside the container, and the container shares your display). Then continue at STEP 11. Steps 4 to 10L apply only to the native Ubuntu route.

## UBUNTU 24.04 LTS x86-64 DESKTOP (NATIVE)



### STEP 5L — Ensure you have a real graphical desktop

  Target: Ubuntu 24.04 LTS x86-64, logged in to an unlocked graphical session as a NON-ROOT user.
  Headless/server-only, root, and Linux ARM64 are outside the current target.
  The launcher requires DISPLAY or WAYLAND_DISPLAY and rejects effective UID 0.


### STEP 6L — Install prerequisites

  Install the x64 .NET 8 runtime and Python 3, plus WebKitGTK 4.1 and GTK 3 runtime libraries. On Ubuntu 24.04, the repository lists libwebkit2gtk-4.1-0 and libgtk-3-0t64. Example:
    sudo apt update
    sudo apt install -y python3 libwebkit2gtk-4.1-0 libgtk-3-0t64
  Install the appropriate .NET 8 runtime from Microsoft's official instructions if not present. Verify:
    dotnet --list-runtimes
    python3 --version
  Note: the .NET/PowerShell runtime versions currently used by v0.4 have reached end-of-support; update dependencies as part of project maintenance rather than claiming long-term supported status.


### STEP 7L — Obtain and extract the BUILT Linux distribution

  You need Start-Linux.sh and app-linux/Bridge and app-linux/bridge-supervisor together in the extracted directory.
  The repository's source checkout does NOT necessarily include app-linux binaries; use an authorized prebuilt distribution or compile with Build-Linux.sh after installing the build dependencies described in docs/LINUX.md.
  Example destination:
    mkdir -p "$HOME/apps/PowerShellGPTBridge-v0.4"
  Extract the distribution there, then:
    cd "$HOME/apps/PowerShellGPTBridge-v0.4"
    chmod u+x Start-Linux.sh app-linux/Bridge app-linux/bridge-supervisor
    test -x ./app-linux/Bridge && echo 'Bridge executable present'


### STEP 8L — Place official Linux tunnel client

  Example, NOT REQUIRED:
    mkdir -p "$HOME/tools"
    # Move the DOWNLOADED official Linux binary into "$HOME/tools/tunnel-client".
    chmod u+x "$HOME/tools/tunnel-client"
    test -x "$HOME/tools/tunnel-client" && echo 'Tunnel client executable present'
  Replace the example location with your actual path. The launcher insists on the executable basename tunnel-client, but NOT any specific directory.


### STEP 9L — Test the local Linux desktop UI

  From the extracted distribution directory:
    ./app-linux/Bridge --ui-test
  This is a local UI-only test, with no remote command execution. The Ubuntu graphical UI remains an acceptance area; if this fails, do not claim verified Linux desktop support.


### STEP 10L — Start the Linux tunnel + bridge

  In an Ubuntu graphical terminal, as a normal user:
    cd "$HOME/apps/PowerShellGPTBridge-v0.4"
    TUNNEL_ID='tunnel_YOUR_REAL_ID'
    TUNNEL_CLIENT="$HOME/tools/tunnel-client"
    ./Start-Linux.sh "$TUNNEL_ID" "$TUNNEL_CLIENT"
  Enter the restricted runtime API key at the LOCAL terminal prompt. Keep that terminal open.
  Do not use sudo to run the bridge.
  Do not use the Windows .exe or Windows Start-Bridge.ps1 launcher on Linux.
  The Linux launcher uses a private temporary directory, reads the key from /dev/tty, and runs the tunnel client with only a limited set of inherited variables.


## CHATGPT CUSTOM MCP PLUGIN (BOTH PLATFORMS)



### STEP 11 — Create or reuse a custom plugin

1. Open https://chatgpt.com and sign in to the ChatGPT workspace associated with the tunnel.
2. Go to Plugins (the exact sidebar wording may vary).
3. Select the plus/add option and choose Add custom MCP server.
4. Choose a Tunnel connection type, NOT a public URL connection.
5. Choose the same tunnel ID created in Step 2.
6. Enter a name, e.g. My PowerShell.
7. Optional description: "Locally approved persistent PowerShell bridge for Windows and Linux."
8. Choose whatever authentication configuration the workspace requires. The Platform Tunnels runtime API key is NOT entered as ChatGPT plugin authentication. Do not assume 'No authentication' is appropriate in all workspaces.
9. Review capabilities and requested permissions carefully.
 10. Create/save the plugin. Enable it for the conversation.
 11. If you already have a working custom plugin linked to the same tunnel, reuse it; you do not need to duplicate it.
  Official help: https://help.openai.com/en/articles/12584461-connecting-custom-mcp-servers-in-chatgpt


### STEP 12 — Check discovered tools

  The plugin should expose these tools where permitted by your account/workspace:
    execute_powershell   (Windows)   or   execute_shell   (Linux)
    ssh_operation
  If no tools appear, verify tunnel connection, workspace association, plugin settings, and client output. Write-capable tool availability may depend on workspace policy.


## PROVE IT WORKS



### STEP 13 — Test one harmless local command

  In ChatGPT, ask:
    "Use My PowerShell to execute exactly Get-Date."   (Windows)
    "Use the bridge to execute exactly: date"          (Linux)
  On Linux the shell is your bash or zsh (PSBRIDGE_SHELL=powershell restores the PowerShell worker). The exact command `date` is the fixed READ operation.
  On the local desktop approval dialog, inspect request details and REJECT the first attempt. Confirm ChatGPT gets the rejection.
  Repeat the command and APPROVE it. Confirm ChatGPT shows a date/time, request ID, machine/identity, and that the operation is in local history.
  NOTE: Never approve an unexpected command or an identity/target you do not recognize.


### STEP 14 — Test persistent shell state

  Ask for one approved execution of:
    $Demo = 123          (Windows PowerShell)      Demo=123          (Linux bash or zsh)
  Then in a SEPARATE approved request ask for:
    $Demo                                          echo $Demo
  Expected output: 123. If not, check session reset/restart status.


### STEP 15 — Optional authorized SSH/SFTP tests

  Use ssh_operation only with a server you own or are authorized to access. Connect first, verify the host fingerprint from an independent trusted channel, then execute a benign command such as pwd. Enter credentials locally. Be aware a timed-out state-changing remote operation may still have run.


## COMMON ISSUES


1. Tunnel ID missing from ChatGPT: check workspace association and permissions; verify the account has Secure MCP Tunnel access.
2. Invalid API key/unauthorized tunnel: key must have Tunnels Read + Use for the intended organization; rotate or recreate a restricted key if necessary.
3. Windows "Missing Bridge.exe": you downloaded source code, not a compiled distribution, or put the launcher in the wrong folder.
4. Windows launch says elevated execution refused: run from standard desktop session, or explicitly opt into elevated execution only if necessary.
5. Linux "Permission denied": run chmod u+x on launcher and the packaged executables. Confirm architecture is Linux x86-64.
6. Linux "Use a non-root graphical desktop session": log into Ubuntu's graphical desktop as a normal user; do not use sudo or a purely headless session.
7. Linux "Missing bridge or official tunnel client": check absolute paths, executable flags, and exact name tunnel-client.
8. GUI fails to show on Linux: the native Linux GUI has not yet passed all acceptance checks. Collect the actual application and session logs. CI screenshot timeout (exit 124) alone does not prove the GUI is broken.
9. Plugin sees no tools: ensure the launcher is running, plugin is enabled, and the correct tunnel/workspace is selected.
10. A command is rejected/expired: each operation requires a separate local approval and has a time limit; don't automatically retry a potentially state-changing operation.
11. Never commit API keys, private config, SSH secrets, logs containing credentials, or local history into GitHub.
12. Stop bridge safely by using its UI disconnect/exit operation. Closing the terminal ends the tunnel and its transient PowerShell session.


## SOURCE AND RELEASE NOTES


GitHub repository:
  https://github.com/yassine-san/PowerShellGPTBridge
Main setup guide:
  https://github.com/yassine-san/PowerShellGPTBridge/blob/main/docs/SETUP.md
Linux guide:
  https://github.com/yassine-san/PowerShellGPTBridge/blob/main/docs/LINUX.md
Windows launcher:
  https://github.com/yassine-san/PowerShellGPTBridge/blob/main/Start-Bridge.ps1
Linux launcher:
  https://github.com/yassine-san/PowerShellGPTBridge/blob/main/Start-Linux.sh
Official tunnel-client releases:
  https://github.com/openai/tunnel-client/releases
Official Secure MCP Tunnel documentation:
  https://developers.openai.com/api/docs/guides/secure-mcp-tunnels
ChatGPT custom MCP help:
  https://help.openai.com/en/articles/12584461-connecting-custom-mcp-servers-in-chatgpt


## CHECKLIST

[ ] Intended organization and workspace selected
[ ] Tunnel created or existing tunnel identified
[ ] Restricted API key created (Tunnels Read + Use)
[ ] Official tunnel-client for the correct OS/architecture downloaded and verified
[ ] Built PowerShellGPTBridge distribution available
[ ] Required OS runtimes installed
[ ] Bridge launched as a normal desktop user
[ ] Runtime key entered locally, not stored in Git
[ ] Custom ChatGPT plugin linked to the correct tunnel
[ ] execute_powershell (Windows) or execute_shell (Linux) is available
[ ] Get-Date (Windows) or date (Linux) rejected once and then approved
[ ] Persistent $Demo test passed
[ ] Any Linux GUI issues recorded separately before claiming fully verified support

