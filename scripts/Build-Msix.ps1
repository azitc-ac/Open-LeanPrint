<#
.SYNOPSIS
    Packages the OpenLeanPrint desktop app as a signed MSIX.

.DESCRIPTION
    Publishes the app, assembles a package layout, patches the manifest and runs
    makeappx (and signtool, when a certificate is given).

    No Windows SDK installation is required: makeappx.exe and signtool.exe come
    from the Microsoft.Windows.SDK.BuildTools NuGet package, pinned by
    packaging/SdkTools/SdkTools.csproj and restored on demand. The package ships
    arm64 binaries too, so this works on Windows on ARM.

    Because PDFium and SkiaSharp are native, an MSIX is architecture-specific -
    build one per -Runtime you want to support.

.PARAMETER Runtime
    Target runtime identifier (win-arm64, win-x64). Default: this machine's.

.PARAMETER Version
    Package version, four parts. Default 1.0.0.0.

.PARAMETER Publisher
    Manifest publisher. MUST equal the signing certificate's subject. Default:
    whatever packaging/AppxManifest.xml already says.

.PARAMETER CertificatePath
    .pfx to sign with (see New-SigningCertificate.ps1). Without it the package
    is built unsigned, which Windows will not install - useful only to inspect
    the layout.

.PARAMETER CertificatePassword
    Password for the .pfx.

.PARAMETER SelfContained
    Bundle the .NET runtime (default), so the package needs nothing installed.

.PARAMETER Output
    Path of the .msix to write. Default: dist\OpenLeanPrint-<runtime>.msix

.EXAMPLE
    .\Build-Msix.ps1 -CertificatePath certs\Alexander-Zarenko.pfx -CertificatePassword "…"
#>
[CmdletBinding()]
param(
    [string]$Runtime,
    [string]$Version = "1.0.0.0",
    [string]$Publisher,
    [string]$CertificatePath,
    [string]$CertificatePassword,
    [bool]$SelfContained = $true,
    [string]$Output
)

$ErrorActionPreference = "Stop"

if (-not $Runtime) {
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    $Runtime = "win-$arch"
}
$packageArchitecture = switch -Wildcard ($Runtime) {
    "*arm64" { "arm64" }
    "*x64"   { "x64" }
    "*x86"   { "x86" }
    default  { throw "Unsupported runtime '$Runtime'." }
}

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo "src\OpenLeanPrint.App\OpenLeanPrint.App.csproj"
$layout = Join-Path $repo "build\msix\$Runtime"
if (-not $Output) { $Output = Join-Path $repo "dist\OpenLeanPrint-$Runtime.msix" }

# --- 1. the packaging tools, straight out of the NuGet cache ---------------
Write-Host "Restoring the Windows SDK packaging tools..." -ForegroundColor Cyan
dotnet restore (Join-Path $repo "packaging\SdkTools\SdkTools.csproj") | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Could not restore the packaging tools." }

$packagesRoot = (dotnet nuget locals global-packages --list) -replace '^global-packages:\s*', ''
$toolsRoot = Join-Path $packagesRoot.Trim() "microsoft.windows.sdk.buildtools"
$makeappx = Get-ChildItem $toolsRoot -Recurse -Filter makeappx.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.DirectoryName -like "*\$packageArchitecture" } |
    Sort-Object FullName -Descending | Select-Object -First 1
$signtool = Get-ChildItem $toolsRoot -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
    Where-Object { $_.DirectoryName -like "*\$packageArchitecture" } |
    Sort-Object FullName -Descending | Select-Object -First 1
if (-not $makeappx) { throw "makeappx.exe for $packageArchitecture not found under $toolsRoot." }
Write-Host "  makeappx: $($makeappx.FullName)"

# --- 2. publish the app into the layout ------------------------------------
Write-Host "Publishing $Runtime (self-contained: $SelfContained)..." -ForegroundColor Cyan
if (Test-Path $layout) { Remove-Item $layout -Recurse -Force }
# Loose files, not single-file: MSIX is the container here.
dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained $SelfContained `
    -p:DebugType=None `
    --output $layout | Out-Null
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed." }

# --- 3. assets + manifest ---------------------------------------------------
Copy-Item (Join-Path $repo "packaging\Assets") (Join-Path $layout "Assets") -Recurse -Force

[xml]$manifest = Get-Content (Join-Path $repo "packaging\AppxManifest.xml")
$identity = $manifest.Package.Identity
$identity.Version = $Version
$identity.ProcessorArchitecture = $packageArchitecture
if ($Publisher) { $identity.Publisher = $Publisher }
$manifest.Save((Join-Path $layout "AppxManifest.xml"))
Write-Host "  identity: $($identity.Name) $Version $packageArchitecture, publisher $($identity.Publisher)"

# --- 4. pack ----------------------------------------------------------------
New-Item -ItemType Directory -Force -Path (Split-Path -Parent $Output) | Out-Null
if (Test-Path $Output) { Remove-Item $Output -Force }
Write-Host "Packing..." -ForegroundColor Cyan
& $makeappx.FullName pack /d $layout /p $Output /o | ForEach-Object { "  $_" }
if ($LASTEXITCODE -ne 0) { throw "makeappx failed with exit code $LASTEXITCODE." }

# --- 5. sign ----------------------------------------------------------------
if ($CertificatePath) {
    if (-not $signtool) { throw "signtool.exe for $packageArchitecture not found." }
    Write-Host "Signing..." -ForegroundColor Cyan
    $arguments = @("sign", "/fd", "SHA256", "/f", $CertificatePath)
    if ($CertificatePassword) { $arguments += @("/p", $CertificatePassword) }
    $arguments += $Output
    & $signtool.FullName @arguments | ForEach-Object { "  $_" }
    if ($LASTEXITCODE -ne 0) { throw "signtool failed with exit code $LASTEXITCODE." }
} else {
    Write-Warning "No -CertificatePath given: the package is unsigned and Windows will refuse to install it."
}

$size = [math]::Round((Get-Item $Output).Length / 1MB, 1)
Write-Host ""
Write-Host "Done: $Output ($size MB)" -ForegroundColor Green
Write-Host "Install with:  Add-AppxPackage `"$Output`""
Write-Host "(the signing certificate must be trusted on that machine first)"
