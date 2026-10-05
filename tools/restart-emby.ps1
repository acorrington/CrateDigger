<#
.SYNOPSIS
    Clean, idempotent restart of the local Emby Server - no orphaned trays,
    no duplicate instances, no port races.

.DESCRIPTION
    Why this exists: naive "Stop-Process EmbyServer; Start-Process EmbyServer"
    cycles left orphaned embytray processes behind (they accumulated as tray
    icons) and could race a tray watchdog into a SECOND server instance. This
    script:

      1. Stops EmbyServer AND embytray (all instances, by name)
      2. Waits until every process is gone AND port 8096 is released
      3. Starts exactly one EmbyServer
      4. Polls /System/Info/Public until healthy
      5. Verifies EXACTLY one server owns port 8096 (fails loudly otherwise)
      6. Starts exactly one embytray (disable with -NoTray)

    Safe to run any time; every step is idempotent.
    ASCII-only on purpose: Windows PowerShell 5.1 decodes BOM-less .ps1 files
    as ANSI, and non-ASCII characters can break parsing.

.EXAMPLE
    powershell -File tools\restart-emby.ps1
    powershell -File tools\restart-emby.ps1 -NoTray
#>
param(
    [switch]$NoTray
)

$ErrorActionPreference = 'Stop'

$sysDir = Join-Path $env:APPDATA 'Emby-Server\system'
$serverExe = Join-Path $sysDir 'EmbyServer.exe'
$trayExe = Join-Path $sysDir 'embytray.exe'

# 1. Stop everything Emby-related (servers AND trays - the debris source).
Write-Host '==> Stopping EmbyServer + embytray (all instances)'
Get-Process -Name EmbyServer, embytray -ErrorAction SilentlyContinue | ForEach-Object {
    Write-Host ("    killing {0} pid={1}" -f $_.Name, $_.Id)
    $_ | Stop-Process -Force
}

# 2. Wait for full teardown: processes gone AND port free.
$deadline = (Get-Date).AddSeconds(30)
while ((Get-Date) -lt $deadline) {
    $procs = @(Get-Process -Name EmbyServer, embytray -ErrorAction SilentlyContinue)
    $port = Get-NetTCPConnection -LocalPort 8096 -State Listen -ErrorAction SilentlyContinue
    if ($procs.Count -eq 0 -and -not $port) { break }
    Start-Sleep -Milliseconds 500
}
$leftover = @(Get-Process -Name EmbyServer, embytray -ErrorAction SilentlyContinue)
if ($leftover.Count -gt 0) {
    $leftover | Stop-Process -Force
    Start-Sleep -Seconds 2
}
if (Get-NetTCPConnection -LocalPort 8096 -State Listen -ErrorAction SilentlyContinue) {
    throw 'Port 8096 still held after teardown - refusing to start a duplicate.'
}
Write-Host '    clean: 0 processes, port 8096 free'

# 3. Start exactly one server.
Write-Host '==> Starting EmbyServer'
Start-Process -FilePath $serverExe -WorkingDirectory $sysDir

# 4. Wait for health.
$healthy = $false
$deadline = (Get-Date).AddSeconds(90)
while ((Get-Date) -lt $deadline) {
    Start-Sleep -Seconds 2
    try {
        $r = Invoke-WebRequest 'http://localhost:8096/System/Info/Public' -UseBasicParsing -TimeoutSec 3
        if ($r.StatusCode -eq 200) { $healthy = $true; break }
    } catch { }
}
if (-not $healthy) { throw 'Emby did not become healthy within 90s.' }
Write-Host '    healthy: HTTP 200'

# 5. Verify exactly one instance owns the port.
$owners = @(Get-NetTCPConnection -LocalPort 8096 -State Listen -ErrorAction SilentlyContinue)
$servers = @(Get-Process -Name EmbyServer -ErrorAction SilentlyContinue)
$ownerPids = @($owners | ForEach-Object OwningProcess | Select-Object -Unique)
if ($servers.Count -ne 1 -or $ownerPids.Count -ne 1) {
    throw ("Duplicate detection: {0} server(s), {1} listener(s) - investigate before proceeding." -f $servers.Count, $ownerPids.Count)
}
Write-Host ("    verified: exactly 1 EmbyServer (pid {0}) owns :8096" -f $servers.Id)

# 6. Exactly one tray (unless disabled).
if (-not $NoTray -and (Test-Path $trayExe)) {
    Get-Process -Name embytray -ErrorAction SilentlyContinue | Stop-Process -Force
    # -WorkingDirectory is CRITICAL: embytray resolves traystrings\en-US.json
    # relative to its CWD. Inheriting the caller's directory (e.g. a repo folder)
    # makes it spam "error opening file" in a console window that keeps reappearing.
    Start-Process $trayExe -ArgumentList '"tray"', '"http://localhost:8096"', '"en-US"' `
        -WorkingDirectory $sysDir -WindowStyle Hidden
    Start-Sleep -Seconds 2
    $trays = @(Get-Process -Name embytray -ErrorAction SilentlyContinue)
    Write-Host ("    tray: {0} instance(s)" -f $trays.Count)
}

Write-Host '==> Done'