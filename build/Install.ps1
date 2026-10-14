# Copyright (C) 2026 Michael Hacker
# SPDX-License-Identifier: GPL-3.0-or-later
# This program is free software: you can redistribute it and/or modify it under the
# terms of the GNU General Public License as published by the Free Software Foundation,
# either version 3 of the License, or (at your option) any later version.
# This program is distributed without any warranty; without even the implied warranty
# of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See <https://www.gnu.org/licenses/>.
<#
.SYNOPSIS
    Installs WSL Container Desktop from the release assets.

.DESCRIPTION
    Download Install.ps1 from the release you want and run it. The published script downloads
    that release's missing .msix and .cer into its own folder. Matching local assets are reused
    for offline installation. A saved script stays pinned to its original release.
    The unstamped repository copy cannot install; use the helper attached to a release.

    NOTE: downloaded files are flagged "from the internet" (Mark of the Web). Under Windows'
    default RemoteSigned execution policy, right-click "Run with PowerShell" will fail with
    "...is not digitally signed". To avoid that, run this script one of these ways:

      powershell -ExecutionPolicy Bypass -File .\Install.ps1

    or unblock the files first, then right-click "Run with PowerShell":

      Unblock-File -Path .\Install.ps1

    Alternatively, skip the script entirely and run the two underlying steps yourself from an
    elevated PowerShell (interactive commands are not subject to script-signing policy):

      Import-Certificate -FilePath .\WSLContainerDesktop-Signing.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
      Add-AppxPackage -Path .\WSLContainerDesktop_<version>_x64.msix -ForceUpdateFromAnyVersion

    This script:
      1. Downloads missing assets from its pinned GitHub release and checks both assets
         against the SHA-256 hashes embedded in the published script.
      2. Trusts the app's signing certificate (LocalMachine\TrustedPeople) so Windows will
         accept the sideloaded package. This requires administrator rights, so the script
         elevates only the certificate import; downloads and app installation stay in your session.
      3. Installs (or updates) the MSIX package.

    The certificate is a self-signed publisher certificate for "CN=Michael Hacker". Review it
    before trusting if you wish (right-click the .cer > Open).
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'

# Replaced only in the release's staged copy, never in the repository source.
$releaseRepository = '__RELEASE_REPOSITORY__'
$releaseVersion = '__RELEASE_VERSION__'
$releaseCertificateSha256 = '__RELEASE_CERTIFICATE_SHA256__'
$releasePackageSha256 = '__RELEASE_PACKAGE_SHA256__'

function Get-InstallerBytesHash {
    param([byte[]]$Bytes)
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { return ([BitConverter]::ToString($algorithm.ComputeHash($Bytes))).Replace('-', '') }
    finally { $algorithm.Dispose() }
}

function Import-InstallerCertificate {
    param([byte[]]$Bytes)

    $encodedCertificate = [Convert]::ToBase64String($Bytes)
    $expectedHash = Get-InstallerBytesHash -Bytes $Bytes
    # Only this fixed command and the captured certificate bytes cross the elevation boundary.
    $command = @'
$ErrorActionPreference = 'Stop'
try {
    $bytes = [Convert]::FromBase64String('__CERTIFICATE__')
    $algorithm = [Security.Cryptography.SHA256]::Create()
    try { $hash = ([BitConverter]::ToString($algorithm.ComputeHash($bytes))).Replace('-', '') }
    finally { $algorithm.Dispose() }
    if ($hash -ne '__HASH__') { throw 'Certificate transfer validation failed.' }
    $certificate = New-Object Security.Cryptography.X509Certificates.X509Certificate2 -ArgumentList @(,$bytes)
    $store = New-Object Security.Cryptography.X509Certificates.X509Store -ArgumentList 'TrustedPeople', 'LocalMachine'
    try {
        $store.Open([Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)
        $store.Add($certificate)
    }
    finally {
        $store.Dispose()
        $certificate.Dispose()
    }
    exit 0
}
catch {
    Write-Error $_
    exit 1
}
'@
    $command = $command.Replace('__CERTIFICATE__', $encodedCertificate).Replace('__HASH__', $expectedHash)
    $options = @{}
    if (-not (Test-InstallerAdministrator)) {
        Write-Host 'Requesting administrator rights to trust the verified publisher certificate...'
        $options.Verb = 'RunAs'
    }
    $encodedCommand = [Convert]::ToBase64String([Text.Encoding]::Unicode.GetBytes($command))
    if ($encodedCommand.Length -gt 30000) {
        throw 'The certificate is too large to transfer safely to the elevated importer. No certificate was trusted.'
    }
    $powershell = Join-Path ([Environment]::SystemDirectory) 'WindowsPowerShell\v1.0\powershell.exe'
    $process = Start-Process -FilePath $powershell -Wait -PassThru @options `
        -ArgumentList @('-NoProfile', '-NonInteractive', '-EncodedCommand', $encodedCommand)
    if ($process.ExitCode -ne 0) {
        throw "Certificate import failed (exit code $($process.ExitCode)). The app was not installed."
    }
}

function Test-InstallerPackageStructure {
    param([IO.Stream]$Stream, [string]$Version, [string]$Publisher)

    Add-Type -AssemblyName System.IO.Compression
    $archive = New-Object IO.Compression.ZipArchive -ArgumentList $Stream, ([IO.Compression.ZipArchiveMode]::Read), $true
    try {
        $entry = $archive.GetEntry('AppxManifest.xml')
        if (-not $entry -or -not $archive.GetEntry('AppxSignature.p7x')) {
            throw 'The package is missing the required MSIX manifest or signature entry. Windows validates the signature during installation.'
        }
        $settings = New-Object Xml.XmlReaderSettings
        $settings.DtdProcessing = [Xml.DtdProcessing]::Prohibit
        $settings.XmlResolver = $null
        $entryStream = $entry.Open()
        $reader = [Xml.XmlReader]::Create($entryStream, $settings)
        try {
            $manifest = New-Object Xml.XmlDocument
            $manifest.XmlResolver = $null
            $manifest.Load($reader)
        }
        finally {
            $reader.Dispose()
            $entryStream.Dispose()
        }
        $identity = $manifest.DocumentElement.SelectSingleNode("*[local-name()='Identity']")
        if ($manifest.DocumentElement.NamespaceURI -ne 'http://schemas.microsoft.com/appx/manifest/foundation/windows10' -or
            -not $identity -or $identity.GetAttribute('Name') -ne '393193CD-4A5B-4502-BC94-7C6AF142CD28' -or
            $identity.GetAttribute('Version') -ne "$Version.0" -or
            $identity.GetAttribute('Publisher') -ne $Publisher -or
            $Publisher -ne 'CN=Michael Hacker') {
            throw "Package identity, publisher, or version does not match WSL Container Desktop $Version."
        }
    }
    finally { $archive.Dispose() }
}

function Test-InstallerAdministrator {
    return ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()
        ).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

function Wait-InstallerCompletion {
    Write-Host 'Press any key to close...'
    [void][System.Console]::ReadKey($true)
}

function Get-InstallerAsset {
    param([string]$Path, [string]$Url, [string]$ReleasePage)

    if (Test-Path -LiteralPath $Path) {
        if (-not (Test-Path -LiteralPath $Path -PathType Leaf) -or
            (Get-Item -LiteralPath $Path).Length -eq 0) {
            throw "Invalid local asset '$Path'. Move it aside and download the matching asset from $ReleasePage."
        }
        Write-Host "Using local asset: $(Split-Path -Leaf $Path)"
        return
    }

    if (-not $Url) {
        throw "Missing local asset '$Path'. The repository script cannot download assets. Get Install.ps1 and matching assets from $ReleasePage."
    }

    $temporaryPath = "$Path.$([Guid]::NewGuid().ToString('N')).download"
    try {
        Write-Host "Downloading $(Split-Path -Leaf $Path) from $Url"
        # TLS 1.2 is needed by GitHub on older Windows PowerShell defaults.
        $previousProtocol = [Net.ServicePointManager]::SecurityProtocol
        try {
            [Net.ServicePointManager]::SecurityProtocol = $previousProtocol -bor [Net.SecurityProtocolType]::Tls12
            Invoke-WebRequest -Uri $Url -OutFile $temporaryPath -UseBasicParsing
        }
        finally {
            [Net.ServicePointManager]::SecurityProtocol = $previousProtocol
        }
        if (-not (Test-Path -LiteralPath $temporaryPath -PathType Leaf) -or
            (Get-Item -LiteralPath $temporaryPath).Length -eq 0) {
            throw 'The download did not produce a non-empty file.'
        }
        # File.Move never overwrites an asset created by another invocation.
        [IO.File]::Move($temporaryPath, $Path)
    }
    catch {
        throw "Could not acquire '$(Split-Path -Leaf $Path)': $($_.Exception.Message) Check internet access and folder write permissions, or download the matching assets manually from $ReleasePage."
    }
    finally {
        if (Test-Path -LiteralPath $temporaryPath) {
            Remove-Item -LiteralPath $temporaryPath -ErrorAction Stop
        }
    }
}

function Invoke-Installation {
    param([string]$ScriptPath)

    $dir = Split-Path -Parent $ScriptPath
    $isSourceScript = $releaseVersion -eq '__RELEASE_VERSION__' -and
        $releaseRepository -eq '__RELEASE_REPOSITORY__' -and
        $releaseCertificateSha256 -eq '__RELEASE_CERTIFICATE_SHA256__' -and
        $releasePackageSha256 -eq '__RELEASE_PACKAGE_SHA256__'
    $releasePage = 'https://github.com/mhackermsft/wslcontainerdesktop/releases'
    $downloadBase = $null
    if ($isSourceScript) {
        throw "The unstamped repository script cannot install or trust local certificates. Download the release-pinned Install.ps1 from $releasePage."
    }
    if ($releaseVersion -notmatch '^\d+\.\d+\.\d+$' -or
        $releaseRepository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$' -or
        $releaseCertificateSha256 -notmatch '^[A-Fa-f0-9]{64}$' -or
        $releasePackageSha256 -notmatch '^[A-Fa-f0-9]{64}$') {
        throw 'Invalid installer release metadata. Download a fresh Install.ps1 from the official release page.'
    }
    $releasePage = "https://github.com/$releaseRepository/releases/tag/v$releaseVersion"
    $downloadBase = "https://github.com/$releaseRepository/releases/download/v$releaseVersion"
    $msixPath = Join-Path $dir "WSLContainerDesktop_${releaseVersion}_x64.msix"
    $packageVersion = $releaseVersion
    Write-Host "Installing WSL Container Desktop $releaseVersion ($releasePage)"

    $cerPath = Join-Path $dir 'WSLContainerDesktop-Signing.cer'
    $cerUrl = $null
    $msixUrl = $null
    if ($downloadBase) {
        $cerUrl = "$downloadBase/WSLContainerDesktop-Signing.cer"
        $msixUrl = "$downloadBase/$(Split-Path -Leaf $msixPath)"
    }
    Get-InstallerAsset -Path $cerPath -Url $cerUrl -ReleasePage $releasePage
    $certificateBytes = [IO.File]::ReadAllBytes($cerPath)
    if ((Get-InstallerBytesHash -Bytes $certificateBytes) -ne $releaseCertificateSha256) {
        throw "The certificate '$cerPath' does not match this release. No certificate was trusted. Move it aside and download the matching certificate from $releasePage."
    }
    $certificate = $null
    try {
        $certificate = New-Object Security.Cryptography.X509Certificates.X509Certificate2 `
            -ArgumentList @(,$certificateBytes)
        $publisher = $certificate.Subject
        Write-Host "Publisher certificate: $($certificate.Subject) (thumbprint $($certificate.Thumbprint))"
    }
    catch {
        throw "Invalid certificate '$cerPath': $($_.Exception.Message) Download the matching certificate from $releasePage."
    }
    finally {
        if ($certificate) { $certificate.Dispose() }
    }
    Get-InstallerAsset -Path $msixPath -Url $msixUrl -ReleasePage $releasePage
    # Deny replacement/writes until Windows finishes consuming the validated package.
    $packageStream = [IO.File]::Open($msixPath, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::Read)
    try {
        $algorithm = [Security.Cryptography.SHA256]::Create()
        try { $packageHash = ([BitConverter]::ToString($algorithm.ComputeHash($packageStream))).Replace('-', '') }
        finally { $algorithm.Dispose() }
        if ($packageHash -ne $releasePackageSha256) {
            throw "The MSIX does not match this release. No certificate was trusted. Move it aside and download the matching package from $releasePage."
        }
        $packageStream.Position = 0
        Test-InstallerPackageStructure -Stream $packageStream -Version $packageVersion -Publisher $publisher
        Unblock-File -LiteralPath $cerPath -ErrorAction Stop
        Unblock-File -LiteralPath $msixPath -ErrorAction Stop
        Import-InstallerCertificate -Bytes $certificateBytes
        Write-Host "Installing package: $(Split-Path -Leaf $msixPath)"
        Add-AppxPackage -Path $msixPath -ForceUpdateFromAnyVersion
    }
    finally { $packageStream.Dispose() }

    Write-Host ''
    Write-Host 'Done. Launch "WSL Container Desktop" from the Start menu.' -ForegroundColor Green
    Wait-InstallerCompletion
}

Invoke-Installation -ScriptPath $PSCommandPath
