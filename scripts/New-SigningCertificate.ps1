<#
.SYNOPSIS
    Creates a self-signed code-signing certificate for MSIX sideloading.

.DESCRIPTION
    MSIX packages must be signed, and the certificate subject must match the
    Publisher in the package manifest exactly. This creates such a certificate
    in the *current user's* store (no administrator rights needed) and exports
    it twice:

      <name>.pfx - the private key, used by Build-Msix.ps1 to sign
      <name>.cer - the public certificate, which has to be trusted once on
                   every machine that installs the package

    Trusting it is the one step that does need administration, because the
    certificate must land in the machine's "Trusted People" store:

      Import-Certificate -FilePath <name>.cer `
          -CertStoreLocation Cert:\LocalMachine\TrustedPeople

    A self-signed certificate is fine for your own machines. Distributing to
    other people properly needs a certificate from a public CA.

.PARAMETER Subject
    Certificate subject. Must equal the manifest's Publisher. Default matches
    packaging/AppxManifest.xml.

.PARAMETER Password
    Password protecting the exported .pfx.

.PARAMETER OutputDirectory
    Where to write the .pfx/.cer. Default: certs\ in the repository root
    (git-ignored - a private key must never be committed).

.PARAMETER YearsValid
    Lifetime in years. Default 3.

.EXAMPLE
    .\New-SigningCertificate.ps1 -Password "correct horse battery staple"
#>
[CmdletBinding()]
param(
    [string]$Subject = "CN=Alexander Zarenko",
    [Parameter(Mandatory = $true)][string]$Password,
    [string]$OutputDirectory,
    [int]$YearsValid = 3
)

$ErrorActionPreference = "Stop"

$repo = Split-Path -Parent $PSScriptRoot
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $repo "certs" }
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null

Write-Host "Creating a code-signing certificate" -ForegroundColor Cyan
Write-Host "  subject : $Subject"
Write-Host "  valid   : $YearsValid years"

$certificate = New-SelfSignedCertificate `
    -Type CodeSigningCert `
    -Subject $Subject `
    -KeyUsage DigitalSignature `
    -KeyAlgorithm RSA `
    -KeyLength 2048 `
    -CertStoreLocation "Cert:\CurrentUser\My" `
    -NotAfter (Get-Date).AddYears($YearsValid) `
    -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}")

$name = ($Subject -replace '^CN=', '') -replace '[^\w\-]', '-'
$pfx = Join-Path $OutputDirectory "$name.pfx"
$cer = Join-Path $OutputDirectory "$name.cer"

$secure = ConvertTo-SecureString -String $Password -Force -AsPlainText
Export-PfxCertificate -Cert $certificate -FilePath $pfx -Password $secure | Out-Null
Export-Certificate -Cert $certificate -FilePath $cer | Out-Null

Write-Host ""
Write-Host "Thumbprint : $($certificate.Thumbprint)"
Write-Host "Private key: $pfx" -ForegroundColor Green
Write-Host "Public cert: $cer" -ForegroundColor Green
Write-Host ""
Write-Host "Trust it once per machine (needs an elevated PowerShell):"
Write-Host "  Import-Certificate -FilePath `"$cer`" -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
Write-Host ""
Write-Host "Keep the .pfx private - it can sign software as you."
