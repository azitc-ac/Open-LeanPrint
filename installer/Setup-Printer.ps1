<#
.SYNOPSIS
    Creates the OpenLeanPrint virtual printer. Run by the installer; safe to run
    by hand from an elevated PowerShell.

.DESCRIPTION
    Windows only creates an IPP queue while the printer is answering, so this
    starts OpenLeanPrint's capture service, waits until it responds, adds the
    printer, and stops the service again. The installed app runs its own service
    from then on.

    It starts the *console* host rather than the desktop app: during
    installation this runs as SYSTEM in session 0, where a windowed process has
    no desktop to start on.

    Needs administrator rights - Add-Printer refuses otherwise, which is the
    whole reason this runs from the installer.

.PARAMETER Exe
    The capture host to use. Defaults to the console host next to this script,
    falling back to the desktop app in headless mode.

.PARAMETER Port
    Loopback port for the IPP service. Default 6310.

.PARAMETER LogPath
    Where to write a transcript. Defaults next to this script, so a failure
    during installation leaves something to read.
#>
[CmdletBinding()]
param(
    [string]$Exe,
    [int]$Port = 6310,
    [string]$LogPath,
    [string]$TaskName
)

$ErrorActionPreference = "Stop"
if (-not $LogPath) { $LogPath = Join-Path $PSScriptRoot "printer-setup.log" }

function Write-Log([string]$message) {
    $line = "{0:HH:mm:ss}  {1}" -f (Get-Date), $message
    Write-Host $line
    try { Add-Content -Path $LogPath -Value $line -ErrorAction SilentlyContinue } catch { }
}

$arguments = @()
if (-not $Exe) {
    $console = Join-Path $PSScriptRoot "OpenLeanPrint.Capture.Host.exe"
    $app = Join-Path $PSScriptRoot "OpenLeanPrint.exe"
    if (Test-Path $console) {
        $Exe = $console
        $arguments = @("--port", "$Port")
    } elseif (Test-Path $app) {
        $Exe = $app
        $arguments = @("--capture-service")
    } else {
        Write-Log "Neither the capture host nor the app was found next to $PSScriptRoot."
        exit 1
    }
}

$url = "http://localhost:$Port/leanprint"

function Test-Endpoint {
    try {
        Invoke-WebRequest $url -TimeoutSec 2 -UseBasicParsing | Out-Null
        return $true
    }
    catch {
        # Any HTTP answer at all means the listener is up.
        return [bool]$_.Exception.Response
    }
}

Write-Log "Setting up the printer using $Exe"

try {
    if (Get-Printer | Where-Object Name -like "*OpenLeanPrint*") {
        Write-Log "The OpenLeanPrint printer already exists - nothing to do."
        exit 0
    }

    # The Windows service normally holds the port by now; only start a private
    # copy if nothing is answering.
    $service = $null
    if (Test-Endpoint) {
        Write-Log "Something is already listening on port $Port - using it."
    }
    else {
        Write-Log "Starting the capture service..."
        $service = Start-Process -FilePath $Exe -ArgumentList $arguments -PassThru -WindowStyle Hidden
    }

    try {
        $ready = $false
        for ($i = 0; $i -lt 40 -and -not $ready; $i++) {
            Start-Sleep -Milliseconds 500
            if ($service -and $service.HasExited) {
                Write-Log "The capture service exited immediately with code $($service.ExitCode)."
                exit 1
            }
            $ready = Test-Endpoint
        }
        if (-not $ready) {
            Write-Log "The capture service never answered on port $Port."
            exit 1
        }
        Write-Log "The service is answering; adding the printer..."

        # Add-Printer does not always raise a terminating error, so ask for one -
        # otherwise a permission failure gets reported later as the wrong thing.
        # It is retried because an installation that has not quite finished can
        # still be holding things up.
        $added = $false
        for ($attempt = 1; $attempt -le 5 -and -not $added; $attempt++) {
            try {
                Add-Printer -IppURL $url -ErrorAction Stop
                $added = $true
            }
            catch {
                Write-Log "Attempt $attempt failed: $($_.Exception.Message)"
                if ($attempt -lt 5) { Start-Sleep -Seconds 10 }
            }
        }
        if (-not $added) {
            Write-Log "Giving up after 5 attempts."
            exit 1
        }

        # Adding is asynchronous in places; confirm rather than assume.
        $found = $null
        for ($i = 0; $i -lt 20 -and -not $found; $i++) {
            Start-Sleep -Milliseconds 500
            $found = Get-Printer | Where-Object Name -like "*OpenLeanPrint*"
        }
        if (-not $found) {
            Write-Log "Add-Printer succeeded but no queue appeared."
            exit 1
        }

        Write-Log "Created: $($found.Name)"
        if ($TaskName) {
            # The scheduled task exists only to do this once.
            Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue
            Write-Log "Removed the scheduled task."
        }
        exit 0
    }
    finally {
        if ($service -and -not $service.HasExited) {
            Stop-Process -Id $service.Id -Force -ErrorAction SilentlyContinue
            Write-Log "Stopped the capture service."
        }
    }
}
catch {
    Write-Log "Unexpected failure: $($_.Exception.Message)"
    exit 1
}
