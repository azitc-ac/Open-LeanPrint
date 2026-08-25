<#
.SYNOPSIS
    Removes the Open-LeanPrint printer created by Register-Printer.ps1.

.DESCRIPTION
    Run elevated ("Run as administrator"). Removes any printer whose name matches
    -Name, or whose port points at the Open-LeanPrint loopback URL.

.PARAMETER Port
    TCP port used when registering. Default 6310.

.PARAMETER ResourcePath
    Resource path used when registering. Default "leanprint".

.PARAMETER Name
    Printer name used when registering. Default "Open-LeanPrint".

.EXAMPLE
    .\Unregister-Printer.ps1 -Port 6310
#>
[CmdletBinding()]
param(
    [int]$Port = 6310,
    [string]$ResourcePath = "leanprint",
    [string]$Name = "Open-LeanPrint"
)

$ErrorActionPreference = "Stop"
$url = "http://localhost:$Port/$ResourcePath"

# Match by printer name, by the URL used as a name, or by the port name.
$printers = Get-Printer | Where-Object {
    $_.Name -eq $Name -or
    $_.Name -eq $url -or
    $_.Name -like "*$ResourcePath*" -or
    $_.PortName -like "*$ResourcePath*"
}

if (-not $printers) {
    Write-Host "No matching Open-LeanPrint printer found."
    return
}

foreach ($p in $printers) {
    Write-Host "Removing printer: $($p.Name)  (port: $($p.PortName))"
    Remove-Printer -Name $p.Name
}
