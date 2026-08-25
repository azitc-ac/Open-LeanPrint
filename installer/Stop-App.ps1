<#
.SYNOPSIS
    Asks a running Open-LeanPrint to stop, before its files are removed.

.DESCRIPTION
    Uninstalling while the app was running had three visible consequences: the
    Restart Manager asked the user to close applications, the removal took about
    two minutes, and the tray icon stayed on screen afterwards. The last one is
    the tell - the notification area only drops an icon when its owner takes it
    away, and a process that is terminated never gets the chance.

    So the app is asked rather than killed. It waits on a named event and shuts
    down through its normal exit path, which disposes the tray icon. Killing is
    the fallback for a copy that is wedged.
#>
[CmdletBinding()]
param(
    [string]$LogPath,
    [int]$TimeoutSeconds = 10
)

$ErrorActionPreference = "Stop"
if (-not $LogPath) { $LogPath = Join-Path $PSScriptRoot "uninstall.log" }

function Write-Log([string]$message) {
    $line = "{0:HH:mm:ss}  {1}" -f (Get-Date), $message
    Write-Host $line
    try { Add-Content -Path $LogPath -Value $line -ErrorAction SilentlyContinue } catch { }
}

try {
    # Which session this runs in decides whether a Local\ event reaches the app,
    # so record it: a quiet failure here is exactly what cost a round trip before.
    Write-Log "Stopping the app. User: $env:USERNAME, session: $((Get-Process -Id $PID).SessionId)."

    $running = @(Get-Process -Name OpenLeanPrint -ErrorAction SilentlyContinue)
    if ($running.Count -eq 0) { Write-Log "Not running."; exit 0 }

    try {
        $quit = [System.Threading.EventWaitHandle]::OpenExisting("Local\OpenLeanPrint.App.Quit")
        $quit.Set() | Out-Null
        Write-Log "Asked $($running.Count) copy/copies to stop."
    }
    catch {
        Write-Log "No copy is listening for the stop signal: $($_.Exception.Message)"
    }

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline -and (Get-Process -Name OpenLeanPrint -ErrorAction SilentlyContinue)) {
        Start-Sleep -Milliseconds 250
    }

    $left = @(Get-Process -Name OpenLeanPrint -ErrorAction SilentlyContinue)
    if ($left.Count -gt 0) {
        Write-Log "Still running after $TimeoutSeconds s; ending it. The tray icon may linger until hovered over."
        $left | Stop-Process -Force -ErrorAction SilentlyContinue
    }
    else {
        Write-Log "Stopped cleanly."
    }
    exit 0
}
catch {
    Write-Log "Could not stop the app: $($_.Exception.Message)"
    exit 0   # never block an uninstall
}
