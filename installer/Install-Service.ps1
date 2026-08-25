<#
.SYNOPSIS
    Registers and starts the Open-LeanPrint capture service.

.DESCRIPTION
    The virtual printer only works while something is listening on the loopback
    IPP port. Leaving that to the desktop app meant the printer was broken
    whenever the app was not running - including right after installing, and
    after anyone quit it from the tray. A printer should work because it exists.

    So the listener runs as a Windows service: starts with the machine, needs
    nobody logged in, and writes captured jobs to a machine-wide folder the app
    can read.

    Registration goes through New-Service rather than sc.exe. sc.exe wants its
    arguments as "key= value" pairs and gets a binary path that contains quotes,
    spaces and a switch; passing that through PowerShell produced exit code 1639
    (invalid command line). The cmdlet takes the path as a parameter and does
    its own quoting.
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

    if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
        Write-Log "Service already registered; replacing it so it runs the current binary."
        try { Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue } catch { }
        & sc.exe delete $ServiceName | Out-Null
        Start-Sleep -Seconds 2
    }

    # The quotes around the executable belong in the value: the path contains a
    # space, and Windows would otherwise read the switch as part of it.
    $binaryPath = '"{0}" --service' -f $Exe
    Write-Log "Registering with binary path: $binaryPath"

    New-Service -Name $ServiceName `
                -BinaryPathName $binaryPath `
                -DisplayName "Open-LeanPrint Capture" `
                -Description "Receives print jobs sent to the Open-LeanPrint virtual printer." `
                -StartupType Automatic | Out-Null

    # If it dies, bring it back: without the listener the printer swallows jobs.
    # Simple tokens only - this is the call that sc.exe parses reliably.
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
