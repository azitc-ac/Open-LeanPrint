<#
.SYNOPSIS
    Registers a local Windows printer that points at the OpenLeanPrint loopback
    IPP capture service, using the in-box Microsoft IPP Class Driver (no
    third-party print driver required, Windows Protected Print compatible).

.DESCRIPTION
    Run the capture host FIRST (so the endpoint is listening), then this script.

    IMPORTANT: run this in an **elevated** PowerShell ("Run as administrator") —
    Add-Printer / Add-PrinterDriver usually require it.

    The script tries the two supported ways to attach the IPP class driver to a
    URL, newest first:
      1. Add-Printer -IppURL <url>                        (Windows 11 / WPP)
      2. Add-Printer -PortName <url> -DriverName "Microsoft IPP Class Driver"

.PARAMETER Port
    TCP port the capture host listens on. Default 6310.

.PARAMETER ResourcePath
    Resource path of the print queue. Default "leanprint".

.PARAMETER Name
    Printer name to create. Default "OpenLeanPrint".

.EXAMPLE
    .\Register-Printer.ps1 -Port 6310
#>
[CmdletBinding()]
param(
    [int]$Port = 6310,
    [string]$ResourcePath = "leanprint",
    [string]$Name = "OpenLeanPrint"
)

$ErrorActionPreference = "Stop"
$url = "http://localhost:$Port/$ResourcePath"

Write-Host "Registering printer '$Name' -> $url"
Write-Host "(Make sure the capture host is running on port $Port first.)"
Write-Host ""

# 1. Make sure the in-box IPP class driver is available.
try {
    $driver = Get-PrinterDriver -Name "Microsoft IPP Class Driver" -ErrorAction SilentlyContinue
    if (-not $driver) {
        Write-Host "Installing 'Microsoft IPP Class Driver'..."
        Add-PrinterDriver -Name "Microsoft IPP Class Driver"
    }
}
catch {
    Write-Warning "Could not verify/add the IPP class driver: $($_.Exception.Message)"
}

$added = $false

# 2. Preferred: Add-Printer -IppURL (Windows 11, Protected Print friendly).
try {
    Add-Printer -IppURL $url
    Write-Host "Success: added via -IppURL."
    $added = $true
}
catch {
    Write-Warning "-IppURL method failed: $($_.Exception.Message)"
}

# 3. Fallback: explicit URL port + IPP class driver.
if (-not $added) {
    try {
        Add-Printer -Name $Name -PortName $url -DriverName "Microsoft IPP Class Driver"
        Write-Host "Success: added via -PortName + IPP class driver."
        $added = $true
    }
    catch {
        Write-Warning "-PortName method failed: $($_.Exception.Message)"
    }
}

Write-Host ""
if ($added) {
    Write-Host "Printer added. Watch the capture host window: it should immediately"
    Write-Host "log a 'Get-Printer-Attributes' request from Windows. Then print to the"
    Write-Host "printer from any app to capture a job."
}
else {
    Write-Host "Automatic registration did not succeed."
    Write-Host "Try the manual method (elevated):"
    Write-Host "    Add-Printer -IppURL $url"
    Write-Host "or see docs/M1-CAPTURE.md for the GUI wizard steps."
}
