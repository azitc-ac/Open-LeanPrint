<#
.SYNOPSIS
    Stops and removes the Open-LeanPrint capture service.
#>
[CmdletBinding()]
param([string]$ServiceName = "OpenLeanPrintCapture")

$ErrorActionPreference = "SilentlyContinue"

if (Get-Service -Name $ServiceName -ErrorAction SilentlyContinue) {
    Write-Host "Stopping $ServiceName..."
    & sc.exe stop $ServiceName | Out-Null
    Start-Sleep -Seconds 2
    & sc.exe delete $ServiceName | Out-Null
}
exit 0
