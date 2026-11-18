#requires -Version 7
[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^tunnel_[a-zA-Z0-9_-]+$')][string] $TunnelId,
    [Parameter(Mandatory)][string] $TunnelClient,
    [switch] $RedactCommands,
    [switch] $LogCommandText,
    [switch] $AllowElevatedGetDate
)
$ErrorActionPreference = 'Stop'
if ($RedactCommands -and $LogCommandText) { throw 'Choose command hashing or command-text logging, not both.' }
if (-not $IsWindows) { throw 'Windows x64 required.' }
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if ($principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator) -and -not $AllowElevatedGetDate) {
    Write-Warning 'Using the configured elevated Windows account. Approved general commands have Administrator privileges.'
}
$tunnel = (Get-Item -LiteralPath $TunnelClient).FullName
if ([IO.Path]::GetFileName($tunnel) -ine 'tunnel-client.exe') { throw 'Select the official tunnel-client.exe.' }
$bridge = (Get-Item -LiteralPath (Join-Path $PSScriptRoot 'app/Bridge.exe')).FullName
Write-Host "Bridge executable: $bridge"
Write-Host "Bridge assembly SHA256: $((Get-FileHash -LiteralPath (Join-Path $PSScriptRoot 'app/Bridge.dll') -Algorithm SHA256).Hash)"
$bridgeCommand = '"' + $bridge.Replace('\','/') + '"'
# Unique private directory also contains the local health socket. No TCP listener.
$work = [IO.DirectoryInfo]::new((Join-Path $env:LOCALAPPDATA ('psb-' + [guid]::NewGuid().ToString('N').Substring(0,12))))
$permissions = [Security.AccessControl.DirectorySecurity]::new()
$permissions.SetAccessRuleProtection($true, $false)
$permissions.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new(
    [Security.Principal.WindowsIdentity]::GetCurrent().User, 'FullControl', 'ContainerInherit,ObjectInherit', 'None', 'Allow'))
[IO.FileSystemAclExtensions]::Create($work, $permissions)
$socketPath = Join-Path $work.FullName 'h.sock'
$profilePath = Join-Path $work.FullName 'tunnel.yaml'
$process = $null
try {
    if ([Text.Encoding]::UTF8.GetByteCount($socketPath) -ge 104) { throw 'Health socket path too long. Use a Windows profile with a shorter LOCALAPPDATA path.' }
    # JSON-quoted strings are valid YAML scalars, including for paths with spaces.
    $yaml = @"
config_version: 1
control_plane:
  base_url: "https://api.openai.com"
  tunnel_id: "$TunnelId"
  api_key: "env:CONTROL_PLANE_API_KEY"
health:
  unix_socket: $(ConvertTo-Json -Compress $socketPath)
admin_ui:
  open_browser: false
mcp:
  stdio_failure_policy: exit
  commands:
    - channel: main
      command: $(ConvertTo-Json -Compress $bridgeCommand)
"@
    [IO.File]::WriteAllText($profilePath, $yaml)
    $start = [Diagnostics.ProcessStartInfo]::new($tunnel)
    $start.UseShellExecute = $false
    $start.WorkingDirectory = $work.FullName
    $start.ArgumentList.Add('run')
    $start.ArgumentList.Add('--config')
    $start.ArgumentList.Add($profilePath)
    # Pass only OS/runtime essentials, so unrelated CLI credentials and tunnel
    # overrides in the interactive terminal do not enter the child environment.
    $start.Environment.Clear()
    foreach ($name in @('SystemRoot','WINDIR','TEMP','TMP','LOCALAPPDATA','APPDATA','USERPROFILE',
            'ProgramData','ProgramFiles','ProgramFiles(x86)','CommonProgramFiles','SystemDrive','PATH',
            'USERNAME','USERDOMAIN','COMPUTERNAME','DOTNET_ROOT','DOTNET_ROOT_X64')) {
        $value = [Environment]::GetEnvironmentVariable($name)
        if ($value) { $start.Environment[$name] = $value }
    }
    if ($AllowElevatedGetDate) {
        Write-Warning 'Elevated Get-Date prototype explicitly enabled. Every request still requires approval.'
        $start.Environment['PSBRIDGE_ALLOW_ELEVATED_GET_DATE'] = '1'
    }
    $start.Environment['PSBRIDGE_TUNNEL_EXE'] = $tunnel
    $start.Environment['PSBRIDGE_HEALTH_SOCKET'] = $socketPath
    if (-not $LogCommandText) { $start.Environment['PSBRIDGE_REDACT_COMMANDS'] = '1' }
    if ($LogCommandText) { Write-Warning '0.3 hashes unrestricted command text in audit logs. Use Keep reviewed details in the manager to retain individual fields.' }
    $secret = Read-Host 'Restricted Platform runtime key (Tunnels Read + Use)' -AsSecureString
    try {
        if ($secret.Length -eq 0) { throw 'A runtime key is required.' }
        $start.Environment['CONTROL_PLANE_API_KEY'] = [Net.NetworkCredential]::new('', $secret).Password
        $process = [Diagnostics.Process]::Start($start)
    } finally {
        [void] $start.Environment.Remove('CONTROL_PLANE_API_KEY')
        $secret.Dispose()
    }
    Write-Host 'Keep this terminal open. Use Disconnect and exit in the bridge window to end the session.'
    while (-not $process.HasExited) { Start-Sleep -Milliseconds 300 }
    Write-Host "Tunnel client exited ($($process.ExitCode)). The PowerShell session has ended."
} finally {
    if ($null -ne $process) {
        if (-not $process.HasExited) { $process.Kill($true); $process.WaitForExit() }
        $process.Dispose()
    }
    if ($work.Exists) { Remove-Item -LiteralPath $work.FullName -Recurse -Force }
}
