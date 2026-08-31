[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string] $PackageDirectory,

    [string] $OutputDirectory = (Join-Path $PSScriptRoot '..\artifacts')
)

$ErrorActionPreference = 'Stop'

$packagePath = (Resolve-Path -LiteralPath $PackageDirectory).Path
$templatePath = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\installer')).Path

$bundles = @(Get-ChildItem -LiteralPath $packagePath -Filter '*.msixbundle' -File)
$certificates = @(Get-ChildItem -LiteralPath $packagePath -Filter '*.cer' -File)

if ($bundles.Count -ne 1) {
    throw "Expected exactly one MSIX bundle in '$packagePath', but found $($bundles.Count)."
}

if ($certificates.Count -ne 1) {
    throw "Expected exactly one certificate in '$packagePath', but found $($certificates.Count)."
}

$bundle = $bundles[0]
$certificateFile = $certificates[0]
$signature = Get-AuthenticodeSignature -LiteralPath $bundle.FullName
if ($null -eq $signature.SignerCertificate) {
    throw "The MSIX bundle '$($bundle.Name)' does not have a readable signing certificate."
}

$certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
    $certificateFile.FullName
)
if ($signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
    throw "The certificate '$($certificateFile.Name)' does not match '$($bundle.Name)'."
}

if ($bundle.BaseName -notmatch '^Plotree_(?<Version>\d+\.\d+\.\d+\.\d+)_') {
    throw "Could not read the Plotree version from '$($bundle.Name)'."
}

$version = $Matches.Version
$resolvedOutput = [System.IO.Path]::GetFullPath($OutputDirectory)
$stagingPath = Join-Path $resolvedOutput "Plotree_${version}_DirectInstaller"
$archivePath = "$stagingPath.zip"

New-Item -ItemType Directory -Path $resolvedOutput -Force | Out-Null

if (Test-Path -LiteralPath $stagingPath) {
    Remove-Item -LiteralPath $stagingPath -Recurse -Force
}

if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}

New-Item -ItemType Directory -Path $stagingPath | Out-Null

try {
    Copy-Item -LiteralPath (Join-Path $templatePath 'Install-Plotree.bat') -Destination $stagingPath
    Copy-Item -LiteralPath (Join-Path $templatePath 'Install-Plotree.ps1') -Destination $stagingPath
    Copy-Item -LiteralPath (Join-Path $templatePath 'README.txt') -Destination $stagingPath
    Copy-Item -LiteralPath $bundle.FullName -Destination $stagingPath
    Copy-Item -LiteralPath $certificateFile.FullName -Destination $stagingPath

    Compress-Archive -Path (Join-Path $stagingPath '*') -DestinationPath $archivePath -CompressionLevel Optimal
}
finally {
    if (Test-Path -LiteralPath $stagingPath) {
        Remove-Item -LiteralPath $stagingPath -Recurse -Force
    }
}

Write-Output $archivePath
