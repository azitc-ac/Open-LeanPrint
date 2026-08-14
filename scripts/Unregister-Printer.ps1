<#
.SYNOPSIS
    Removes the OpenLeanPrint printer connection created by Register-Printer.ps1.

.PARAMETER Port
    TCP port used when registering. Default 6310.

.PARAMETER ResourcePath
    Resource path used when registering. Default "leanprint".

.EXAMPLE
    .\Unregister-Printer.ps1 -Port 6310
#>
[CmdletBinding()]
param(
    [int]$Port = 6310,
    [string]$ResourcePath = "leanprint"
)

$ErrorActionPreference = "Stop"
$url = "http://localhost:$Port/$ResourcePath"

$printer = Get-Printer | Where-Object { $_.Name -eq $url -or $_.Name -like "*$ResourcePath*" }
if ($null -eq $printer) {
    Write-Host "No matching OpenLeanPrint printer found."
    return
}

foreach ($p in $printer) {
    Write-Host "Removing printer: $($p.Name)"
    Remove-Printer -Name $p.Name
}
