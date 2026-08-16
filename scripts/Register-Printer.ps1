<#
.SYNOPSIS
    Registers a local Windows printer that points at the OpenLeanPrint loopback
    IPP capture service, using the in-box Microsoft IPP Class Driver (no
    third-party print driver required, Windows Protected Print compatible).

.DESCRIPTION
    Run the capture host FIRST (so the endpoint is listening), then this script
    in an ELEVATED PowerShell ("Run as administrator").

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

# --- Elevation check (Add-Printer needs admin) ---
$isAdmin = ([Security.Principal.WindowsPrincipal] `
    [Security.Principal.WindowsIdentity]::GetCurrent()
    ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
if (-not $isAdmin) {
    Write-Warning "This script must run in an ELEVATED PowerShell."
    Write-Host   "Right-click PowerShell -> 'Run as administrator', cd into the repo, re-run."
    return
}

Write-Host "Registering printer '$Name' -> $url"

# --- Reachability check: is the capture host actually listening? ---
$reachable = $false
try {
    $tcp = New-Object System.Net.Sockets.TcpClient
    $tcp.Connect("localhost", $Port)
    $reachable = $tcp.Connected
}
catch { $reachable = $false }
finally { if ($tcp) { $tcp.Close() } }

if (-not $reachable) {
    Write-Warning "Nothing is listening on localhost:$Port."
    Write-Host   "Start the capture host first, in another window:"
    Write-Host   "    dotnet run --project src/OpenLeanPrint.Capture.Host -- --port $Port"
    Write-Host   "Then re-run this script. (Windows validates the URL during add, so the host must be up.)"
    return
}
Write-Host "Capture host is reachable on localhost:$Port. Adding printer..."
Write-Host ""

# --- Ensure the in-box IPP class driver is available ---
try {
    if (-not (Get-PrinterDriver -Name "Microsoft IPP Class Driver" -ErrorAction SilentlyContinue)) {
        Write-Host "Installing 'Microsoft IPP Class Driver'..."
        Add-PrinterDriver -Name "Microsoft IPP Class Driver"
    }
}
catch {
    Write-Warning "Could not verify/add the IPP class driver: $($_.Exception.Message)"
}

function Test-PrinterPresent {
    $null -ne (Get-Printer -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -eq $Name -or $_.Name -eq $url -or
        $_.Name -like "*$ResourcePath*" -or $_.PortName -like "*$ResourcePath*"
    })
}

# --- Attempt 1: Add-Printer -IppURL (creates the IPP port + queue in one step) ---
Write-Host "Attempt 1: Add-Printer -IppURL ..."
try { Add-Printer -IppURL $url } catch { Write-Warning "  -IppURL raised: $($_.Exception.Message)" }
Start-Sleep -Seconds 2

# --- Attempt 2 (fallback): explicit port + IPP class driver ---
if (-not (Test-PrinterPresent)) {
    Write-Host "Attempt 2: Add-Printer -PortName + Microsoft IPP Class Driver ..."
    try {
        Add-Printer -Name $Name -PortName $url -DriverName "Microsoft IPP Class Driver"
    }
    catch { Write-Warning "  -PortName raised: $($_.Exception.Message)" }
    Start-Sleep -Seconds 2
}

Write-Host ""

# --- Verify actual result (do NOT trust the cmdlet not to throw) ---
if (Test-PrinterPresent) {
    Write-Host "SUCCESS: printer is present:"
    Get-Printer | Where-Object {
        $_.Name -eq $Name -or $_.Name -eq $url -or
        $_.Name -like "*$ResourcePath*" -or $_.PortName -like "*$ResourcePath*"
    } | Format-Table Name, PortName, DriverName, PrinterStatus -AutoSize
    Write-Host "Now print to it from any app; watch the capture host window."
}
else {
    Write-Warning "FAILED: no printer was created."
    Write-Host "Windows could reach the host (checked above) but did not accept the queue."
    Write-Host "Please copy the capture host's log lines from this attempt so the IPP"
    Write-Host "attribute set can be adjusted. See docs/M1-CAPTURE.md."
}
