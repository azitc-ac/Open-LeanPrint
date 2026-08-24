<#
.SYNOPSIS
    Removes what the capture service left in ProgramData.

.DESCRIPTION
    Uninstalling removed the program but left C:\ProgramData\OpenLeanPrint
    behind, captured jobs and log and all.

    Nothing in there is worth keeping. A captured job is spool output on its way
    from the print queue into the app's window - the document it came from is
    still wherever it was printed from, and the app deletes each file as soon as
    it has read it anyway. What is left when the program goes is the handful
    nobody got round to printing.
#>
[CmdletBinding()]
param(
    [string]$DataFolder = (Join-Path $env:ProgramData "OpenLeanPrint"),
    [string]$LogPath
)

$ErrorActionPreference = "Stop"
if (-not $LogPath) { $LogPath = Join-Path $PSScriptRoot "uninstall.log" }

function Write-Log([string]$message) {
    $line = "{0:HH:mm:ss}  {1}" -f (Get-Date), $message
    Write-Host $line
    try { Add-Content -Path $LogPath -Value $line -ErrorAction SilentlyContinue } catch { }
}

try {
    if (-not (Test-Path $DataFolder)) { Write-Log "Nothing in $DataFolder."; exit 0 }

    $files = @(Get-ChildItem $DataFolder -Recurse -File -ErrorAction SilentlyContinue)
    Remove-Item $DataFolder -Recurse -Force -ErrorAction Stop
    Write-Log "Removed $DataFolder ($($files.Count) file(s))."
    exit 0
}
catch {
    Write-Log "Could not remove $DataFolder : $($_.Exception.Message)"
    exit 0   # never block an uninstall
}
