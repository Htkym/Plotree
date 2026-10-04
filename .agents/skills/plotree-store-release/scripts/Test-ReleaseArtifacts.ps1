[CmdletBinding()]
param(
    [Parameter(Mandatory)][ValidatePattern('^\d+\.\d+\.\d+\.\d+$')][string] $Version,
    [string] $RepositoryRoot = (Join-Path $PSScriptRoot '../../../..'),
    [switch] $VerifyDirectInstaller
)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Security
$repo = (Resolve-Path -LiteralPath $RepositoryRoot).Path
[xml]$sourceManifest = Get-Content -LiteralPath (Join-Path $repo 'src/Plotree/Package.appxmanifest') -Raw
$expected = $sourceManifest.Package.Identity
if ($expected.Version -ne $Version) { throw 'Requested version differs from the source manifest.' }
$packageDir = Join-Path $repo "src/Plotree/AppPackages/$Version"
$bundles = @(Get-ChildItem -LiteralPath $packageDir -Filter '*.msixbundle' -File)
$uploads = @(Get-ChildItem -LiteralPath $packageDir -Filter '*.msixupload' -File)
if ($bundles.Count -ne 1 -or $uploads.Count -ne 1) { throw 'Expected exactly one bundle and one Store upload container.' }
$bundle = $bundles[0]
$upload = $uploads[0]
function Read-ZipXml($zip, $name) {
    $entry = $zip.GetEntry($name)
    if ($null -eq $entry) { throw "Missing XML entry: $name" }
    $reader = [System.IO.StreamReader]::new($entry.Open())
    try { [xml]$reader.ReadToEnd() } finally { $reader.Dispose() }
}
function Get-EntryHash($entry) {
    $stream = $entry.Open()
    $sha = [System.Security.Cryptography.SHA256]::Create()
    try { [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
    finally { $stream.Dispose(); $sha.Dispose() }
}
function Test-BlockMap($zip) {
    $map = Read-ZipXml $zip 'AppxBlockMap.xml'
    if ($map.BlockMap.HashMethod -ne 'http://www.w3.org/2001/04/xmlenc#sha256') { throw 'Unsupported block-map hash method.' }
    $sha = [System.Security.Cryptography.SHA256]::Create()
    $files = 0; $blocks = 0
    try {
        foreach ($file in $map.BlockMap.File) {
            $name = $file.Name.Replace('\', '/')
            $entry = $zip.GetEntry($name)
            if ($null -eq $entry -or $entry.Length -ne [long]$file.Size) { throw "Block-map entry/size mismatch: $name" }
            $stream = $entry.Open()
            try {
                $buffer = New-Object byte[] 65536
                foreach ($block in $file.Block) {
                    $count = 0
                    while ($count -lt $buffer.Length) {
                        $read = $stream.Read($buffer, $count, $buffer.Length - $count)
                        if ($read -eq 0) { break }
                        $count += $read
                    }
                    if ([Convert]::ToBase64String($sha.ComputeHash($buffer, 0, $count)) -ne $block.Hash) { throw "Block-map digest mismatch: $name" }
                    $blocks++
                }
                if ($stream.ReadByte() -ne -1) { throw "Unverified payload bytes: $name" }
            } finally { $stream.Dispose() }
            $files++
        }
    } finally { $sha.Dispose() }
    [pscustomobject]@{ Files = $files; Blocks = $blocks; AllSHA256Valid = $true }
}
$zip = [System.IO.Compression.ZipFile]::OpenRead($bundle.FullName)
try {
    $signatureEntry = $zip.GetEntry('AppxSignature.p7x')
    if ($null -eq $signatureEntry) { throw 'The outer bundle is unsigned.' }
    $stream = $signatureEntry.Open(); $memory = [System.IO.MemoryStream]::new()
    try { $stream.CopyTo($memory); $bytes = $memory.ToArray() } finally { $stream.Dispose(); $memory.Dispose() }
    if ($bytes.Length -lt 5 -or [System.Text.Encoding]::ASCII.GetString($bytes, 0, 4) -ne 'PKCX') { throw 'Unexpected MSIX signature header.' }
    $cms = [System.Security.Cryptography.Pkcs.SignedCms]::new()
    $cms.Decode($bytes[4..($bytes.Length - 1)])
    $cms.CheckSignature($true)
    if ($cms.SignerInfos.Count -ne 1) { throw 'Unexpected bundle signer count.' }
    $signer = $cms.SignerInfos[0].Certificate
    if ($signer.Subject -ne $expected.Publisher) { throw 'Signing certificate subject differs from the manifest Publisher.' }
    if ($signer.NotAfter -lt (Get-Date) -or $signer.NotBefore -gt (Get-Date)) { throw 'Signing certificate is outside its validity interval.' }
    $manifest = Read-ZipXml $zip 'AppxMetadata/AppxBundleManifest.xml'
    if ($manifest.Bundle.Identity.Name -ne $expected.Name -or $manifest.Bundle.Identity.Publisher -ne $expected.Publisher -or $manifest.Bundle.Identity.Version -ne $Version) { throw 'Bundle identity differs from source.' }
    $packages = @()
    foreach ($package in $manifest.Bundle.Packages.Package) {
        if ($package.Type -ne 'application') { continue }
        $entry = $zip.GetEntry($package.FileName)
        if ($null -eq $entry -or $package.Version -ne $Version) { throw 'Missing or incorrectly versioned architecture payload.' }
        $memory = [System.IO.MemoryStream]::new(); $stream = $entry.Open()
        try { $stream.CopyTo($memory) } finally { $stream.Dispose() }
        $memory.Position = 0
        $payload = [System.IO.Compression.ZipArchive]::new($memory, [System.IO.Compression.ZipArchiveMode]::Read)
        try {
            $app = Read-ZipXml $payload 'AppxManifest.xml'
            $identity = $app.Package.Identity
            if ($identity.Name -ne $expected.Name -or $identity.Publisher -ne $expected.Publisher -or $identity.Version -ne $Version -or $identity.ProcessorArchitecture -ne $package.Architecture) { throw 'Application payload identity mismatch.' }
            $languages = @($app.Package.Resources.Resource | ForEach-Object Language | Sort-Object -Unique)
            if ('en-US' -notin $languages -or 'ja-JP' -notin $languages) { throw 'Expected English/Japanese resources are absent.' }
            $names = @($payload.Entries | ForEach-Object FullName)
            foreach ($requiredFile in @('Plotree.exe', 'System.Private.CoreLib.dll', 'Microsoft.UI.Xaml.dll')) {
                if (-not ($names | Where-Object { [System.IO.Path]::GetFileName($_) -eq $requiredFile })) { throw "Self-contained runtime file missing: $requiredFile" }
            }
            $packages += [pscustomobject]@{ Architecture = $package.Architecture; Version = $identity.Version; Languages = $languages; SelfContainedFilesPresent = $true; BlockMap = (Test-BlockMap $payload) }
        } finally { $payload.Dispose(); $memory.Dispose() }
    }
    if (($packages.Architecture | Sort-Object) -join ',' -ne 'arm64,x64') { throw 'Expected exactly x64 and ARM64 application packages.' }
    $bundleBlockMap = Test-BlockMap $zip
} finally { $zip.Dispose() }
$bundleHash = (Get-FileHash -LiteralPath $bundle.FullName -Algorithm SHA256).Hash
$zip = [System.IO.Compression.ZipFile]::OpenRead($upload.FullName)
try {
    $entries = @($zip.Entries | Where-Object FullName -Like '*.msixbundle')
    if ($entries.Count -ne 1 -or (Get-EntryHash $entries[0]) -ne $bundleHash) { throw 'Store upload does not contain the final signed bundle.' }
    $uploadEntries = @($zip.Entries | ForEach-Object FullName)
} finally { $zip.Dispose() }
$artifacts = @($bundle.FullName, $upload.FullName)
$directVerified = $false
if ($VerifyDirectInstaller) {
    $directPath = Join-Path $repo "artifacts/Plotree_${Version}_DirectInstaller.zip"
    $zip = [System.IO.Compression.ZipFile]::OpenRead($directPath)
    try {
        $bundleEntries = @($zip.Entries | Where-Object FullName -Like '*.msixbundle')
        $certEntries = @($zip.Entries | Where-Object FullName -Like '*.cer')
        foreach ($requiredEntry in @('Install-Plotree.bat', 'Install-Plotree.ps1', 'README.txt')) {
            if ($null -eq $zip.GetEntry($requiredEntry)) { throw "Missing direct-installer entry: $requiredEntry" }
        }
        if ($zip.Entries.Count -ne 5 -or $bundleEntries.Count -ne 1 -or $certEntries.Count -ne 1 -or (Get-EntryHash $bundleEntries[0]) -ne $bundleHash) { throw 'Unexpected direct-installer contents or bundle digest.' }
        $stream = $certEntries[0].Open(); $memory = [System.IO.MemoryStream]::new()
        try { $stream.CopyTo($memory); $publicCertificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new($memory.ToArray()) }
        finally { $stream.Dispose(); $memory.Dispose() }
        try { if ($publicCertificate.Thumbprint -ne $signer.Thumbprint) { throw 'Direct-installer certificate differs from the signer.' } }
        finally { $publicCertificate.Dispose() }
        $directVerified = $true
    } finally { $zip.Dispose() }
    $artifacts += $directPath
}
[pscustomobject]@{
    Version = $Version; Identity = $expected.Name; Publisher = $expected.Publisher
    CmsSignatureValid = $true; WindowsPolicyStatus = (Get-AuthenticodeSignature -LiteralPath $bundle.FullName).Status.ToString()
    Packages = $packages; BundleBlockMap = $bundleBlockMap
    UploadContainsFinalSignedBundle = $true; UploadEntries = $uploadEntries; DirectInstallerVerified = $directVerified
    Artifacts = @($artifacts | ForEach-Object { [pscustomobject]@{ File = [System.IO.Path]::GetFileName($_); Bytes = (Get-Item -LiteralPath $_).Length; SHA256 = (Get-FileHash -LiteralPath $_ -Algorithm SHA256).Hash } })
}
