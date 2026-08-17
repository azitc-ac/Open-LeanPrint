<#
.SYNOPSIS
    Publishes the OpenLeanPrint desktop app as a single executable.

.DESCRIPTION
    Produces a self-contained, single-file build that runs on a machine with no
    .NET installed - which is what makes it distributable without an installer.
    Native dependencies (PDFium, SkiaSharp) are bundled, so -Runtime must match
    the target machine: win-arm64 for Windows on ARM, win-x64 for Intel/AMD.

    An MSIX package would additionally need makeappx.exe and signtool.exe from
    the Windows SDK plus a code-signing certificate; see docs/M4-APP.md.

.PARAMETER Runtime
    Target runtime identifier. Default: the architecture of this machine.

.PARAMETER SelfContained
    Bundle the .NET runtime (default). With -SelfContained:$false the result is
    much smaller but needs the .NET 8 Desktop Runtime on the target machine.

.PARAMETER Output
    Output folder. Default: dist\<runtime> in the repository root.

.EXAMPLE
    .\Publish-App.ps1
    .\Publish-App.ps1 -Runtime win-x64
    .\Publish-App.ps1 -SelfContained:$false
#>
[CmdletBinding()]
param(
    [string]$Runtime,
    [bool]$SelfContained = $true,
    [string]$Output
)

$ErrorActionPreference = "Stop"

if (-not $Runtime) {
    $arch = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
    $Runtime = "win-$arch"
}

$repo = Split-Path -Parent $PSScriptRoot
$project = Join-Path $repo "src\OpenLeanPrint.App\OpenLeanPrint.App.csproj"
if (-not $Output) { $Output = Join-Path $repo "dist\$Runtime" }

Write-Host "Publishing OpenLeanPrint" -ForegroundColor Cyan
Write-Host "  runtime        : $Runtime"
Write-Host "  self-contained : $SelfContained"
Write-Host "  output         : $Output"
Write-Host ""

dotnet publish $project `
    --configuration Release `
    --runtime $Runtime `
    --self-contained $SelfContained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=None `
    --output $Output

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }

$exe = Join-Path $Output "OpenLeanPrint.exe"
if (-not (Test-Path $exe)) { throw "Expected $exe, but it was not produced." }

$size = [math]::Round((Get-Item $exe).Length / 1MB, 1)
Write-Host ""
Write-Host "Done: $exe ($size MB)" -ForegroundColor Green
Write-Host "Copy that single file anywhere and run it."
