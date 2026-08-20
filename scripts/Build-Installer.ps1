<#
.SYNOPSIS
    Builds the OpenLeanPrint installer (.msi).

.DESCRIPTION
    Publishes the desktop app self-contained, then packages it with WiX. The
    installer sets up the virtual printer during installation, which the app
    cannot do on its own: creating a printer queue needs administrator rights,
    and an installer already has them.

    WiX comes from NuGet, so nothing has to be installed first - the .NET SDK is
    enough.

.PARAMETER Runtime
    Target runtime identifier. Default: this machine's architecture.

.PARAMETER Output
    Where to copy the finished .msi. Default: dist\ in the repository root.

.PARAMETER CertificateSubject
    Sign the installer with this certificate from your store, e.g.
    "CN=Alexander Zarenko". Unsigned installers make Windows SmartScreen
    complain, so this is worth doing even for your own machines.

.EXAMPLE
    .\Build-Installer.ps1 -CertificateSubject "CN=Alexander Zarenko"
#>
[CmdletBinding()]
param(
    [string]$Runtime,
    [string]$Output,
    [string]$CertificateSubject
)

$ErrorActionPreference = "Stop"

if (-not $Runtime) {
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    $Runtime = "win-$arch"
}

$repo = Split-Path -Parent $PSScriptRoot
$installer = Join-Path $repo "installer"
$publish = Join-Path $installer "publish"
if (-not $Output) { $Output = Join-Path $repo "dist" }

Write-Host "Building the OpenLeanPrint installer" -ForegroundColor Cyan
Write-Host "  runtime : $Runtime"

# --- 1. the app itself ------------------------------------------------------
if (Test-Path $publish) { Remove-Item $publish -Recurse -Force }
dotnet publish (Join-Path $repo "src\OpenLeanPrint.App\OpenLeanPrint.App.csproj") `
    --configuration Release --runtime $Runtime --self-contained true `
    -p:DebugType=None --output $publish | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

$exe = Join-Path $publish "OpenLeanPrint.exe"
if (-not (Test-Path $exe)) { throw "Expected $exe after publishing." }
Write-Host "  published $((Get-ChildItem $publish -Recurse -File).Count) files"

# --- 2. the licence shown by the installer ----------------------------------
# WiX wants RTF; the repository keeps plain MIT text.
$license = Get-Content (Join-Path $repo "LICENSE") -Raw
$escaped = $license -replace '\\', '\\\\' -replace '([{}])', '\$1' -replace "`r`n", '\par '
@"
{\rtf1\ansi\deff0{\fonttbl{\f0\fnil\fcharset0 Segoe UI;}}
\f0\fs18 $escaped
}
"@ | Set-Content (Join-Path $installer "License.rtf") -Encoding ASCII

# --- 3. the package ---------------------------------------------------------
$version = ([xml](Get-Content (Join-Path $repo "Directory.Build.props"))).Project.PropertyGroup.Version
if (-not $version) { $version = "0.0.0" }
Write-Host "  version : $version"

dotnet build (Join-Path $installer "OpenLeanPrint.Installer.wixproj") `
    --configuration Release -v minimal -p:ProductVersion=$version
if ($LASTEXITCODE -ne 0) { throw "The WiX build failed." }

$msi = Get-ChildItem (Join-Path $installer "bin") -Recurse -Filter "OpenLeanPrint.msi" |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
if (-not $msi) { throw "No .msi was produced." }

New-Item -ItemType Directory -Force -Path $Output | Out-Null
$target = Join-Path $Output "OpenLeanPrint-$Runtime.msi"
Copy-Item $msi.FullName $target -Force

# --- 4. signing -------------------------------------------------------------
if ($CertificateSubject) {
    $packages = (dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', ''
    $signtool = Get-ChildItem (Join-Path $packages.Trim() "microsoft.windows.sdk.buildtools") -Recurse -Filter signtool.exe |
        Where-Object { $_.DirectoryName -like "*\x64" } | Sort-Object FullName -Descending | Select-Object -First 1
    if (-not $signtool) { throw "signtool.exe not found - run scripts\Build-Msix.ps1 once to restore it." }

    & $signtool.FullName sign /fd SHA256 /n ($CertificateSubject -replace '^CN=', '') $target | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Signing failed." }
    Write-Host "  signed with $CertificateSubject"
}

$size = [math]::Round((Get-Item $target).Length / 1MB, 1)
Write-Host ""
Write-Host "Done: $target ($size MB)" -ForegroundColor Green
Write-Host "Installing it sets up the virtual printer as well - no further steps."
