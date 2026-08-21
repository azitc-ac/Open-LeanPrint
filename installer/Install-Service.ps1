<#
.SYNOPSIS
    Registers and starts the OpenLeanPrint capture service.

.DESCRIPTION
    The virtual printer only works while something is listening on the loopback
    IPP port. Leaving that to the desktop app meant the printer was broken
    whenever the app was not running - including right after installing, and
    after anyone quit it from the tray. A printer should work because it exists.

    So the listener runs as a Windows service: starts with the machine, needs
    nobody logged in, and writes captured jobs to a machine-wide folder the app
    can read.
#>
[CmdletBinding()]
param(
    [string]$Exe,
    [string]$ServiceName = "OpenLeanPrintCapture",
    [string]$LogPath
)

$ErrorActionPreference = "Stop"
if (-not $Exe) { $Exe = Join-Path $PSScriptRoot "OpenLeanPrint.Capture.Host.exe" }
if (-not $LogPath) { $LogPath = Join-Path $PSScriptRoot "service-setup.log" }

function Write-Log([string]$message) {
    $line = "{0:HH:mm:ss}  {1}" -f (Get-Date), $message
    Write-Host $line
    try { Add-Content -Path $LogPath -Value $line -ErrorAction SilentlyContinue } catch { }
}

try {
    if (-not (Test-Path $Exe)) { Write-Log "Capture host not found at $Exe."; exit 1 }

    $existing = Get-Service -Name $ServiceName -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Log "Service already registered; making sure it runs the current binary."
        & sc.exe stop $ServiceName | Out-Null
        Start-Sleep -Seconds 2
        & sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 2
    }

    # sc.exe rather than New-Service: it takes the argument on the binary path
    # in the form Windows itself uses, and is available everywhere.
    $binPath = '"{0}" --service' -f $Exe
    & sc.exe create $ServiceName binPath= $binPath start= auto DisplayName= "OpenLeanPrint Capture" | Out-Null
    if ($LASTEXITCODE -ne 0) { Write-Log "sc create failed with exit code $LASTEXITCODE."; exit 1 }

    & sc.exe description $ServiceName "Receives print jobs sent to the OpenLeanPrint virtual printer." | Out-Null

    # If it dies, bring it back: without the listener the printer swallows jobs.
    & sc.exe failure $ServiceName reset= 86400 actions= restart/5000/restart/10000/restart/30000 | Out-Null

    Start-Service -Name $ServiceName
    $service = Get-Service -Name $ServiceName
    Write-Log "Service '$ServiceName' is $($service.Status)."
    exit 0
}
catch {
    Write-Log "Could not install the service: $($_.Exception.Message)"
    exit 1
}
