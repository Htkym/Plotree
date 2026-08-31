[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

function Get-SingleFile {
    param(
        [Parameter(Mandatory)]
        [string] $Filter,

        [Parameter(Mandatory)]
        [string] $Description
    )

    $files = @(Get-ChildItem -LiteralPath $PSScriptRoot -Filter $Filter -File)
    if ($files.Count -ne 1) {
        throw "Expected exactly one $Description in the installer folder, but found $($files.Count)."
    }

    return $files[0]
}

try {
    $bundle = Get-SingleFile -Filter '*.msixbundle' -Description 'MSIX bundle'
    $certificateFile = Get-SingleFile -Filter '*.cer' -Description 'signing certificate'

    $signature = Get-AuthenticodeSignature -LiteralPath $bundle.FullName
    if ($null -eq $signature.SignerCertificate) {
        throw 'The MSIX bundle does not have a readable signing certificate.'
    }

    $certificate = [System.Security.Cryptography.X509Certificates.X509Certificate2]::new(
        $certificateFile.FullName
    )

    if ($signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) {
        throw 'The included certificate does not match the certificate used to sign the MSIX bundle.'
    }

    $now = Get-Date
    if ($now -lt $certificate.NotBefore -or $now -gt $certificate.NotAfter) {
        throw "The signing certificate is not valid at the current date. Valid period: $($certificate.NotBefore) - $($certificate.NotAfter)."
    }

    $principal = [Security.Principal.WindowsPrincipal]::new(
        [Security.Principal.WindowsIdentity]::GetCurrent()
    )
    $isAdministrator = $principal.IsInRole(
        [Security.Principal.WindowsBuiltInRole]::Administrator
    )

    if (
        $signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid -and
        -not $isAdministrator
    ) {
        Write-Host 'Administrator approval is required to trust the Plotree signing certificate.'
        $arguments = "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`""
        $process = Start-Process powershell.exe -Verb RunAs -ArgumentList $arguments -Wait -PassThru
        exit $process.ExitCode
    }

    if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
        Write-Warning 'The GitHub version uses a self-signed certificate.'
        Write-Host 'The certificate will be added to Local Computer\Trusted People.'
        Write-Host "Certificate thumbprint: $($certificate.Thumbprint)"
        $confirmation = Read-Host 'Type Y to trust this certificate and install Plotree'
        if ($confirmation -notmatch '^(?i:y|yes)$') {
            throw 'Installation was cancelled.'
        }

        Import-Certificate `
            -FilePath $certificateFile.FullName `
            -CertStoreLocation 'Cert:\LocalMachine\TrustedPeople' | Out-Null

        $signature = Get-AuthenticodeSignature -LiteralPath $bundle.FullName
        if ($signature.Status -ne [System.Management.Automation.SignatureStatus]::Valid) {
            throw "The MSIX bundle signature is still not trusted: $($signature.StatusMessage)"
        }
    }

    Write-Host "Installing $($bundle.Name)..."
    Add-AppxPackage -Path $bundle.FullName -ForceApplicationShutdown
    Write-Host 'Plotree was installed successfully.'
}
catch {
    Write-Error $_ -ErrorAction Continue
    exit 1
}
