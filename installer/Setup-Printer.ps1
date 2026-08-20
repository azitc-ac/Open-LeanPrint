<#
.SYNOPSIS
    Creates the OpenLeanPrint virtual printer. Run by the installer; safe to run
    by hand from an elevated PowerShell.

.DESCRIPTION
    Windows only creates an IPP queue while the printer is answering, so this
    starts OpenLeanPrint's capture service headlessly, waits until it responds,
    adds the printer, and stops the service again. The installed app starts its
    own service from then on.

    Needs administrator rights: Add-Printer refuses otherwise. The installer is
    already elevated, which is the whole reason this runs there.

.PARAMETER Exe
    Path to OpenLeanPrint.exe. Defaults to the folder this script sits in.

.PARAMETER Port
    Loopback port for the IPP service. Default 6310.
#>
[CmdletBinding()]
param(
    [string]$Exe,
    [int]$Port = 6310
)

$ErrorActionPreference = "Stop"
if (-not $Exe) { $Exe = Join-Path $PSScriptRoot "OpenLeanPrint.exe" }
$url = "http://localhost:$Port/leanprint"

if (-not (Test-Path $Exe)) { throw "OpenLeanPrint.exe not found at $Exe." }

# Already there? Then there is nothing to do - reinstalling must not create a
# second queue.
if (Get-Printer | Where-Object Name -like "*OpenLeanPrint*") {
    Write-Host "The OpenLeanPrint printer already exists."
    exit 0
}

Write-Host "Starting the capture service..."
$service = Start-Process -FilePath $Exe -ArgumentList "--capture-service" -PassThru

try {
    # Wait for it to answer; a fresh process needs a moment.
    $ready = $false
    for ($i = 0; $i -lt 30 -and -not $ready; $i++) {
        Start-Sleep -Milliseconds 500
        try {
            Invoke-WebRequest $url -TimeoutSec 2 -UseBasicParsing | Out-Null
            $ready = $true
        } catch {
            # Any HTTP answer at all means the listener is up.
            if ($_.Exception.Response) { $ready = $true }
        }
    }
    if (-not $ready) { throw "The capture service did not start listening on port $Port." }

    Write-Host "Adding the printer..."
    # Add-Printer does not always raise a terminating error, so ask for one -
    # otherwise a permission failure would be reported later as the wrong thing.
    try {
        Add-Printer -IppURL $url -ErrorAction Stop
    }
    catch {
        throw "Could not create the printer: $($_.Exception.Message) " +
              "(this step needs administrator rights)."
    }

    # Adding is asynchronous in places; confirm rather than assume.
    $found = $null
    for ($i = 0; $i -lt 20 -and -not $found; $i++) {
        Start-Sleep -Milliseconds 500
        $found = Get-Printer | Where-Object Name -like "*OpenLeanPrint*"
    }
    if (-not $found) { throw "Add-Printer reported success but no queue appeared." }

    Write-Host "Created: $($found.Name)"
}
finally {
    if ($service -and -not $service.HasExited) {
        Stop-Process -Id $service.Id -Force -ErrorAction SilentlyContinue
    }
}
