# Copyright (C) 2026 Michael Hacker
# SPDX-License-Identifier: GPL-3.0-or-later
# This program is free software: you can redistribute it and/or modify it under the
# terms of the GNU General Public License as published by the Free Software Foundation,
# either version 3 of the License, or (at your option) any later version.
# This program is distributed without any warranty; without even the implied warranty
# of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See <https://www.gnu.org/licenses/>.
<#
.SYNOPSIS
    Dependency-free installer checks. Does not elevate, trust certificates, or install packages.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $root 'build\Install.ps1'
$stagePath = Join-Path $root 'build\Stage-InstallScript.ps1'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) "WslInstallerTests-$([Guid]::NewGuid().ToString('N'))"
$script:caseNumber = 0

function Assert-True {
    param([bool]$Condition, [string]$Message)
    if (-not $Condition) { throw $Message }
}

function Assert-Error {
    param([scriptblock]$Action, [string]$Pattern)
    $message = $null
    try { & $Action }
    catch { $message = $_.Exception.Message }
    Assert-True ($null -ne $message -and $message -match $Pattern) "Expected error matching '$Pattern', got '$message'."
}

function Write-Certificate {
    [IO.File]::WriteAllBytes((Join-Path $script:caseDir 'WSLContainerDesktop-Signing.cer'), $script:certificateBytes)
}

function Write-Package {
    param([string]$Version = '9.8.7')
    [IO.File]::WriteAllBytes((Join-Path $script:caseDir "WSLContainerDesktop_${Version}_x64.msix"), $script:packageBytes)
}

function Invoke-Case {
    param([string]$Name, [scriptblock]$Body, [switch]$Source)
    $script:caseNumber++
    $script:caseDir = Join-Path $testRoot "case $script:caseNumber"
    New-Item -ItemType Directory -Path $script:caseDir | Out-Null
    $scriptPath = Join-Path $script:caseDir 'Install.ps1'
    if ($Source) {
        Copy-Item -LiteralPath $sourcePath -Destination $scriptPath
    }
    else {
        & $stagePath -Version '9.8.7' -Repository 'mhackermsft/wslcontainerdesktop' `
            -CertificatePath $fixturePath -PackagePath $packageFixturePath -OutputPath $scriptPath
    }

    $content = Get-Content -LiteralPath $scriptPath -Raw
    $tokens = $null
    $errors = $null
    $ast = [Management.Automation.Language.Parser]::ParseInput($content, [ref]$tokens, [ref]$errors)
    Assert-True ($errors.Count -eq 0) 'Installer must parse.'
    $lastStatement = $ast.EndBlock.Statements[-1]
    Assert-True ($lastStatement.Extent.Text -eq 'Invoke-Installation -ScriptPath $PSCommandPath') 'Expected explicit installer entry point.'
    # Load declarations only, then replace every machine-changing operation.
    . ([scriptblock]::Create($content.Substring(0, $lastStatement.Extent.StartOffset)))
    if (-not $Source) {
        Assert-True ($releaseVersion -eq '9.8.7') 'Staging must stamp the release version.'
        Assert-True ($releaseRepository -eq 'mhackermsft/wslcontainerdesktop') 'Staging must stamp the repository.'
        Assert-True ($releaseCertificateSha256 -eq (Get-FileHash -LiteralPath $fixturePath -Algorithm SHA256).Hash) 'Staging must stamp the exact exported certificate hash.'
        Assert-True ($releasePackageSha256 -eq (Get-FileHash -LiteralPath $packageFixturePath -Algorithm SHA256).Hash) 'Staging must stamp the exact MSIX hash.'
    }
    $script:downloads = @()
    $script:imports = @()
    $script:installs = @()
    $script:unblocked = @()
    $script:elevations = @()
    $script:admin = $true
    $script:downloadFailure = $null
    $script:emptyDownload = $false
    $script:importImplementation = ${function:Import-InstallerCertificate}
    $script:importExitCode = 0

    function Test-InstallerAdministrator { return $script:admin }
    function Wait-InstallerCompletion {}
    function Start-Process {
        param($FilePath, $Verb, $ArgumentList, [switch]$Wait, [switch]$PassThru)
        Assert-True ($Wait -and $PassThru) 'Privileged helper must wait and return its exit code.'
        $script:elevations += ,@($FilePath, $Verb, $ArgumentList)
        return [pscustomobject]@{ ExitCode = $script:importExitCode }
    }
    function Invoke-WebRequest {
        param($Uri, $OutFile, [switch]$UseBasicParsing)
        $script:downloads += $Uri
        if ($script:emptyDownload) {
            [IO.File]::WriteAllBytes($OutFile, [byte[]]@())
        }
        elseif ($Uri -match '\.cer$') {
            [IO.File]::WriteAllBytes($OutFile, $script:certificateBytes)
        }
        else {
            [IO.File]::WriteAllBytes($OutFile, $script:packageBytes)
        }
        if ($script:downloadFailure) { throw $script:downloadFailure }
    }
    function Unblock-File {
        param($LiteralPath, $ErrorAction)
        $script:unblocked += $LiteralPath
    }
    function Import-InstallerCertificate {
        param([byte[]]$Bytes)
        Assert-True ((Get-InstallerBytesHash $Bytes) -eq (Get-InstallerBytesHash $script:certificateBytes)) 'Trust only verified bytes.'
        $script:imports += ,$Bytes
    }
    function Add-AppxPackage {
        param($Path, [switch]$ForceUpdateFromAnyVersion)
        Assert-True $ForceUpdateFromAnyVersion.IsPresent 'Preserve installation options.'
        Assert-Error { [IO.File]::WriteAllText($Path, 'replacement') } 'being used|another process'
        $script:installs += $Path
    }

    & $Body $scriptPath
    Assert-True (@(Get-ChildItem -LiteralPath $script:caseDir -Filter '*.download').Count -eq 0) 'Owned temporary downloads must be cleaned up.'
    Write-Host "PASS: $Name"
}

try {
    New-Item -ItemType Directory -Path $testRoot | Out-Null
    # Generate a certificate entirely in memory, never in a Windows certificate store.
    $rsa = New-Object Security.Cryptography.RSACng -ArgumentList 2048
    $request = New-Object Security.Cryptography.X509Certificates.CertificateRequest -ArgumentList @(
        'CN=Michael Hacker', $rsa, [Security.Cryptography.HashAlgorithmName]::SHA256,
        [Security.Cryptography.RSASignaturePadding]::Pkcs1)
    $certificate = $request.CreateSelfSigned([DateTimeOffset]::UtcNow.AddDays(-1), [DateTimeOffset]::UtcNow.AddDays(1))
    $script:certificateBytes = $certificate.Export([Security.Cryptography.X509Certificates.X509ContentType]::Cert)
    $certificate.Dispose()
    $rsa.Dispose()
    $fixturePath = Join-Path $testRoot 'fixture.cer'
    [IO.File]::WriteAllBytes($fixturePath, $script:certificateBytes)
    Add-Type -AssemblyName System.IO.Compression
    $packageFixturePath = Join-Path $testRoot 'fixture.msix'
    $stream = [IO.File]::Create($packageFixturePath)
    $archive = New-Object IO.Compression.ZipArchive -ArgumentList $stream, ([IO.Compression.ZipArchiveMode]::Create)
    try {
        $entry = $archive.CreateEntry('AppxManifest.xml')
        $writer = New-Object IO.StreamWriter -ArgumentList $entry.Open()
        try {
            $writer.Write('<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"><Identity Name="393193CD-4A5B-4502-BC94-7C6AF142CD28" Publisher="CN=Michael Hacker" Version="9.8.7.0" /></Package>')
        }
        finally { $writer.Dispose() }
        [void]$archive.CreateEntry('AppxSignature.p7x')
    }
    finally {
        $archive.Dispose()
        $stream.Dispose()
    }
    # The fixture models MSIX structure, not a cryptographically valid signature.
    $script:packageBytes = [IO.File]::ReadAllBytes($packageFixturePath)

    Invoke-Case 'Script alone downloads its two pinned assets' {
        param($path)
        Invoke-Installation -ScriptPath $path
        Assert-True ($script:downloads.Count -eq 2) 'Expected two downloads.'
        Assert-True ($script:downloads[0] -eq 'https://github.com/mhackermsft/wslcontainerdesktop/releases/download/v9.8.7/WSLContainerDesktop-Signing.cer') 'Certificate URL must be pinned.'
        Assert-True ($script:downloads[1] -eq 'https://github.com/mhackermsft/wslcontainerdesktop/releases/download/v9.8.7/WSLContainerDesktop_9.8.7_x64.msix') 'Package URL must be pinned.'
        Assert-True ($script:imports.Count -eq 1 -and $script:installs.Count -eq 1) 'Expected successful mocked install.'
        Assert-True ($script:unblocked.Count -eq 2) 'Unblock only selected assets.'
    }
    Invoke-Case 'Certificate only downloads MSIX' {
        param($path)
        Write-Certificate
        Invoke-Installation -ScriptPath $path
        Assert-True ($script:downloads.Count -eq 1 -and $script:downloads[0] -match '\.msix$') 'Download only the missing MSIX.'
    }
    Invoke-Case 'MSIX only downloads certificate' {
        param($path)
        Write-Package
        Invoke-Installation -ScriptPath $path
        Assert-True ($script:downloads.Count -eq 1 -and $script:downloads[0] -match '\.cer$') 'Download only the missing certificate.'
    }
    Invoke-Case 'Matching assets work offline' {
        param($path)
        Write-Certificate
        Write-Package
        $script:downloadFailure = 'Network unavailable'
        Invoke-Installation -ScriptPath $path
        Assert-True ($script:downloads.Count -eq 0 -and $script:installs.Count -eq 1) 'Offline installation must not use the network.'
    }
    Invoke-Case 'Other versions and unrelated files are ignored' {
        param($path)
        Write-Package -Version '99.0.0'
        [IO.File]::WriteAllText((Join-Path $script:caseDir 'unrelated.cer'), 'unrelated')
        Invoke-Installation -ScriptPath $path
        Assert-True ($script:downloads.Count -eq 2) 'Do not infer latest or select unrelated files.'
        Assert-True ($script:installs[0] -match '9\.8\.7_x64\.msix$') 'Saved script must stay pinned.'
        Assert-True ([IO.File]::ReadAllText((Join-Path $script:caseDir 'unrelated.cer')) -eq 'unrelated') 'Unrelated assets must remain untouched.'
    }
    Invoke-Case 'Wrong certificate stops before download of MSIX or trust' {
        param($path)
        [IO.File]::WriteAllText((Join-Path $script:caseDir 'WSLContainerDesktop-Signing.cer'), 'wrong certificate')
        Assert-Error { Invoke-Installation -ScriptPath $path } 'does not match this release'
        Assert-True ($script:downloads.Count -eq 0 -and $script:imports.Count -eq 0 -and $script:installs.Count -eq 0) 'Mismatch must not lead to network, trust, or installation.'
    }
    Invoke-Case 'Failed partial certificate download leaves no final asset' {
        param($path)
        $script:downloadFailure = 'Connection interrupted'
        Assert-Error { Invoke-Installation -ScriptPath $path } 'Connection interrupted.*manually'
        Assert-True (-not (Test-Path -LiteralPath (Join-Path $script:caseDir 'WSLContainerDesktop-Signing.cer'))) 'Partial certificate must not become a final asset.'
        Assert-True ($script:imports.Count -eq 0 -and $script:installs.Count -eq 0) 'No mutation after failed download.'
    }
    Invoke-Case 'Failed MSIX download does not trust the acquired certificate' {
        param($path)
        Write-Certificate
        $script:downloadFailure = 'HTTP 404'
        Assert-Error { Invoke-Installation -ScriptPath $path } 'HTTP 404.*releases/tag/v9.8.7'
        Assert-True ($script:imports.Count -eq 0 -and $script:installs.Count -eq 0) 'No trust after missing package.'
    }
    Invoke-Case 'Empty downloads are rejected' {
        param($path)
        $script:emptyDownload = $true
        Assert-Error { Invoke-Installation -ScriptPath $path } 'non-empty'
        Assert-True ($script:imports.Count -eq 0) 'Do not trust empty downloads.'
    }
    Invoke-Case 'Invalid local package is not overwritten' {
        param($path)
        Write-Certificate
        [IO.File]::WriteAllBytes((Join-Path $script:caseDir 'WSLContainerDesktop_9.8.7_x64.msix'), [byte[]]@())
        Assert-Error { Invoke-Installation -ScriptPath $path } 'Invalid local asset'
        Assert-True ($script:downloads.Count -eq 0 -and $script:imports.Count -eq 0) 'Do not overwrite invalid local files or trust after failure.'
    }
    Invoke-Case 'Write failures report manual fallback' {
        param($path)
        function Invoke-WebRequest {
            param($Uri, $OutFile, [switch]$UseBasicParsing)
            throw (New-Object UnauthorizedAccessException 'Access denied')
        }
        Assert-Error { Invoke-Installation -ScriptPath $path } 'Access denied.*folder write permissions'
        Assert-True ($script:imports.Count -eq 0) 'No trust after write failure.'
    }
    Invoke-Case 'Concurrent asset creation is not overwritten' {
        param($path)
        function Invoke-WebRequest {
            param($Uri, $OutFile, [switch]$UseBasicParsing)
            [IO.File]::WriteAllBytes($OutFile, $script:certificateBytes)
            $finalPath = $OutFile -replace '\.[a-f0-9]{32}\.download$', ''
            [IO.File]::WriteAllText($finalPath, 'concurrent asset')
        }
        Assert-Error { Invoke-Installation -ScriptPath $path } 'Could not acquire'
        Assert-True ([IO.File]::ReadAllText((Join-Path $script:caseDir 'WSLContainerDesktop-Signing.cer')) -eq 'concurrent asset') 'Do not overwrite files from another invocation.'
        Assert-True ($script:imports.Count -eq 0) 'Do not trust after failed finalization.'
    }
    Invoke-Case 'Elevation imports bytes, never executes the mutable script' {
        param($path)
        $script:admin = $false
        & $script:importImplementation -Bytes $script:certificateBytes
        Assert-True ($script:elevations.Count -eq 1) 'Elevate only certificate import.'
        $arguments = $script:elevations[0][2]
        Assert-True ($arguments -contains '-EncodedCommand' -and $arguments -notcontains '-File') 'Never rerun a writable script elevated.'
        $command = [Text.Encoding]::Unicode.GetString([Convert]::FromBase64String($arguments[-1]))
        Assert-True ($command.Contains([Convert]::ToBase64String($script:certificateBytes))) 'Transfer captured certificate bytes.'
        Assert-True ($command.Contains((Get-InstallerBytesHash $script:certificateBytes))) 'Recheck certificate transfer.'
        Assert-True (-not $command.Contains($path)) 'Do not reopen files from the download folder.'
        $errors = $null
        $tokens = $null
        [void][Management.Automation.Language.Parser]::ParseInput($command, [ref]$tokens, [ref]$errors)
        Assert-True ($errors.Count -eq 0) 'Privileged import command must parse.'
    }
    Invoke-Case 'Import failure propagates explicitly' {
        param($path)
        $script:admin = $false
        $script:importExitCode = 1
        Assert-Error { & $script:importImplementation -Bytes $script:certificateBytes } 'Certificate import failed'
    }
    Invoke-Case 'Failed elevated import prevents package installation' {
        param($path)
        $script:admin = $false
        $script:importExitCode = 1
        function Import-InstallerCertificate {
            param([byte[]]$Bytes)
            & $script:importImplementation -Bytes $Bytes
        }
        Assert-Error { Invoke-Installation -ScriptPath $path } 'Certificate import failed'
        Assert-True ($script:installs.Count -eq 0) 'Never install after privileged import failure.'
    }
    Invoke-Case 'Cancelled administrator prompt prevents package installation' {
        param($path)
        $script:admin = $false
        function Start-Process {
            param($FilePath, $Verb, $ArgumentList, [switch]$Wait, [switch]$PassThru)
            throw 'Administrator approval cancelled'
        }
        function Import-InstallerCertificate {
            param([byte[]]$Bytes)
            & $script:importImplementation -Bytes $Bytes
        }
        Assert-Error { Invoke-Installation -ScriptPath $path } 'Administrator approval cancelled'
        Assert-True ($script:installs.Count -eq 0) 'Never install after cancelled approval.'
    }
    Invoke-Case 'Certificate replacement after validation does not change imported bytes' {
        param($path)
        function Import-InstallerCertificate {
            param([byte[]]$Bytes)
            [IO.File]::WriteAllText((Join-Path $script:caseDir 'WSLContainerDesktop-Signing.cer'), 'replacement')
            Assert-True ((Get-InstallerBytesHash $Bytes) -eq (Get-InstallerBytesHash $script:certificateBytes)) 'Import captured verified certificate, not swapped file.'
            $script:imports += ,$Bytes
        }
        Invoke-Installation -ScriptPath $path
        Assert-True ($script:installs.Count -eq 1) 'Captured certificate should permit installation.'
    }
    Invoke-Case 'Tampered nonempty MSIX is rejected before certificate trust' {
        param($path)
        Write-Certificate
        [IO.File]::WriteAllText((Join-Path $script:caseDir 'WSLContainerDesktop_9.8.7_x64.msix'), 'not an MSIX')
        Assert-Error { Invoke-Installation -ScriptPath $path } 'MSIX does not match this release'
        Assert-True ($script:imports.Count -eq 0) 'Do not trust a certificate before verifying MSIX.'
    }
    Invoke-Case 'Package structure must match selected version' {
        param($path)
        Write-Certificate
        Write-Package
        $releaseVersion = '1.2.3'
        [IO.File]::Move((Join-Path $script:caseDir 'WSLContainerDesktop_9.8.7_x64.msix'),
            (Join-Path $script:caseDir 'WSLContainerDesktop_1.2.3_x64.msix'))
        Assert-Error { Invoke-Installation -ScriptPath $path } 'identity, publisher, or version'
        Assert-True ($script:imports.Count -eq 0) 'Reject mismatched package identity before trust.'
    }
    Invoke-Case 'Unstamped source refuses trust even with valid local assets' -Source {
        param($path)
        Write-Certificate
        Write-Package
        Assert-Error { Invoke-Installation -ScriptPath $path } 'unstamped repository script cannot install'
        Assert-True ($script:downloads.Count -eq 0 -and $script:installs.Count -eq 0 -and
            $script:imports.Count -eq 0 -and $script:elevations.Count -eq 0) 'Unstamped script must not acquire, elevate, trust, or install.'
    }
    Invoke-Case 'Unstamped source rejects ambiguous versions' -Source {
        param($path)
        Write-Package
        Write-Package -Version '1.2.3'
        Assert-Error { Invoke-Installation -ScriptPath $path } 'unstamped repository script cannot install'
        Assert-True ($script:imports.Count -eq 0 -and $script:downloads.Count -eq 0) 'Source script must not guess a release.'
    }
    Invoke-Case 'Unstamped source explains missing certificate' -Source {
        param($path)
        Write-Package
        Assert-Error { Invoke-Installation -ScriptPath $path } 'unstamped repository script cannot install'
        Assert-True ($script:downloads.Count -eq 0) 'Source script must not download.'
    }
    Invoke-Case 'Unstamped source rejects malformed certificate' -Source {
        param($path)
        Write-Package
        [IO.File]::WriteAllText((Join-Path $script:caseDir 'WSLContainerDesktop-Signing.cer'), 'not a certificate')
        Assert-Error { Invoke-Installation -ScriptPath $path } 'unstamped repository script cannot install'
        Assert-True ($script:imports.Count -eq 0 -and $script:installs.Count -eq 0) 'Do not import malformed certificates.'
    }
    Invoke-Case 'Malformed metadata fails before elevation' {
        param($path)
        $releaseVersion = '__BROKEN__'
        Assert-Error { Invoke-Installation -ScriptPath $path } 'Invalid installer release metadata'
        Assert-True ($script:elevations.Count -eq 0 -and $script:downloads.Count -eq 0) 'Invalid metadata must stop early.'
    }

    $workflow = Get-Content -LiteralPath (Join-Path $root '.github\workflows\release.yml') -Raw
    Assert-True ($workflow -match '(?m)^concurrency:\r?\n  group: release-publication\r?\n  cancel-in-progress: false\r?$') 'Serialize releases across refs without cancelling an active publication.'
    Assert-True ($workflow -match 'Stage-InstallScript\.ps1 -Version \$ver') 'Workflow must stage the versioned helper.'
    Assert-True ($workflow -match '\$ver = "\$\{\{ steps\.ver\.outputs\.version \}\}"') 'Stage the same version as the release.'
    $publicationMatch = [regex]::Match($workflow, "(?ms)^          if \('\$\{\{ github.event.inputs.prerelease \}\}' -eq 'true'\) \{.*?^          \}\r?$")
    Assert-True $publicationMatch.Success 'Expected explicit release promotion block.'
    function gh {
        $global:LASTEXITCODE = 0
        return 'v9.8.6'
    }
    foreach ($prerelease in @('true', 'false')) {
        $ghArgs = @()
        $block = $publicationMatch.Value.Replace('${{ github.event.inputs.prerelease }}', $prerelease).
            Replace('${{ steps.ver.outputs.version }}', '9.8.7')
        . ([scriptblock]::Create($block))
        if ($prerelease -eq 'true') {
            Assert-True ($ghArgs -contains '--prerelease' -and $ghArgs -contains '--latest=false') 'Prereleases must not become latest.'
        }
        else {
            Assert-True ($ghArgs -contains '--latest' -and $ghArgs -notcontains '--prerelease') 'Newer stable releases must become latest.'
        }
    }
    $promotionHelper = Join-Path $root 'build\Get-ReleaseLatestFlag.ps1'
    Assert-True ((& $promotionHelper -Version '9.8.7' -LatestTag '') -eq '--latest') 'First stable release should be latest.'
    Assert-True ((& $promotionHelper -Version '9.8.7' -LatestTag 'v9.8.6') -eq '--latest') 'Newer stable release should be latest.'
    Assert-True ((& $promotionHelper -Version '9.8.7' -LatestTag 'v10.0.0') -eq '--latest=false') 'Maintenance release must not regress latest.'
    Assert-True ((& $promotionHelper -Version '9.8.7' -LatestTag 'v9.8.7') -eq '--latest=false') 'Equal version must not replace latest.'
    Assert-Error { & $promotionHelper -Version '9.8.7' -LatestTag 'unexpected' } 'Refusing to change latest'
    Write-Host "PASS: Release staging and stable/prerelease publication"
    Write-Host "All $($script:caseNumber + 1) installer checks passed."
}
finally {
    if (Test-Path -LiteralPath $testRoot) {
        Remove-Item -LiteralPath $testRoot -Recurse -Force
    }
}
