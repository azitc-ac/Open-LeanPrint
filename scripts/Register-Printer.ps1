<#
.SYNOPSIS
    Registers a local Windows printer that points at the OpenLeanPrint loopback
    IPP capture service, using the in-box Microsoft IPP Class Driver (no
    third-party print driver required).

.DESCRIPTION
    OpenLeanPrint captures print jobs by hosting a loopback IPP endpoint and
    letting Windows drive it with its built-in IPP class driver. This script
    adds a printer connection to that endpoint.

    NOTE: This script is a convenience wrapper and is still to be validated on
    Windows on ARM / x64. If it does not create a working printer, use the
    reliable manual method documented in docs/M1-CAPTURE.md ("Add a printer by
    URL").

    Run the capture host FIRST (so the endpoint is listening), then this script.

.PARAMETER Port
    TCP port the capture host listens on. Default 6310.

.PARAMETER ResourcePath
    Resource path of the print queue. Default "leanprint".

.EXAMPLE
    .\Register-Printer.ps1 -Port 6310
#>
[CmdletBinding()]
param(
    [int]$Port = 6310,
    [string]$ResourcePath = "leanprint"
)

$ErrorActionPreference = "Stop"

# Windows reaches the loopback IPP service over HTTP; the IPP client is invoked
# via the "shared printer by URL" mechanism.
$url = "http://localhost:$Port/$ResourcePath"

Write-Host "Registering OpenLeanPrint printer -> $url"
Write-Host "(Make sure the capture host is running on port $Port first.)"

try {
    # This uses Windows' Internet Printing / IPP client to attach to the URL.
    Add-Printer -ConnectionName $url
    Write-Host "Printer connection added. Look for it in the printer list."
    Write-Host "If it is missing or does not print, use the manual method in docs/M1-CAPTURE.md."
}
catch {
    Write-Warning "Automatic registration failed: $($_.Exception.Message)"
    Write-Host ""
    Write-Host "Reliable manual method:"
    Write-Host "  1. Settings > Bluetooth & devices > Printers & scanners > Add device"
    Write-Host "  2. 'The printer that I want isn't listed' / Add manually"
    Write-Host "  3. 'Select a shared printer by name' and enter:"
    Write-Host "       $url"
    Write-Host "  4. Finish the wizard (Windows uses its IPP class driver)."
}
