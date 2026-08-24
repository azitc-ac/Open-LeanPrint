<#
.SYNOPSIS
    Tidies up what the capture service left in ProgramData.

.DESCRIPTION
    Uninstalling removed the program but left C:\ProgramData\OpenLeanPrint
    behind, log and all.

    Captured jobs are the user's own documents and are deliberately not deleted -
    somebody's unprinted payslip is not ours to throw away on the way out. The
    service log is ours, so that goes, and the folders go too if nothing of the
    user's is left in them. Uninstall on a machine with nothing pending therefore
    leaves nothing behind.
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

    foreach ($ours in @("service.log")) {
        $path = Join-Path $DataFolder $ours
        if (Test-Path $path) { Remove-Item $path -Force -ErrorAction SilentlyContinue }
    }

    $captured = Join-Path $DataFolder "captured"
    $left = @(Get-ChildItem $captured -File -ErrorAction SilentlyContinue)
    if ($left.Count -gt 0) {
        Write-Log "Keeping $($left.Count) captured job(s) in $captured - they are your documents."
        exit 0
    }

    Remove-Item $captured -Force -Recurse -ErrorAction SilentlyContinue
    Remove-Item $DataFolder -Force -Recurse -ErrorAction SilentlyContinue
    Write-Log "Removed $DataFolder."
    exit 0
}
catch {
    Write-Log "Could not tidy up $DataFolder : $($_.Exception.Message)"
    exit 0
}
