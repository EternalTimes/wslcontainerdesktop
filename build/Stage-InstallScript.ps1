# Copyright (C) 2026 Michael Hacker
# SPDX-License-Identifier: GPL-3.0-or-later
# This program is free software: you can redistribute it and/or modify it under the
# terms of the GNU General Public License as published by the Free Software Foundation,
# either version 3 of the License, or (at your option) any later version.
# This program is distributed without any warranty; without even the implied warranty
# of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See <https://www.gnu.org/licenses/>.
<#
.SYNOPSIS
    Stamps the release installer with its version, repository, and both asset hashes.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$')]
    [string]$Repository,
    [Parameter(Mandatory = $true)]
    [string]$CertificatePath,
    [Parameter(Mandatory = $true)]
    [string]$PackagePath,
    [Parameter(Mandatory = $true)]
    [string]$OutputPath
)

$ErrorActionPreference = 'Stop'
$content = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'Install.ps1') -Raw
$metadata = @{
    RELEASE_REPOSITORY = $Repository
    RELEASE_VERSION = $Version
    RELEASE_CERTIFICATE_SHA256 = (Get-FileHash -LiteralPath $CertificatePath -Algorithm SHA256).Hash
    RELEASE_PACKAGE_SHA256 = (Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash
}
foreach ($name in $metadata.Keys) {
    $placeholder = "__${name}__"
    # Replace the declaration only; source-script detection retains its literal markers.
    $pattern = "(?m)^(\`$release[A-Za-z0-9]+ = ')$placeholder(')\r?$"
    if ([regex]::Matches($content, $pattern).Count -ne 1) {
        throw "Expected exactly one declaration for $placeholder in Install.ps1."
    }
    $content = [regex]::Replace($content, $pattern, "`${1}$($metadata[$name])`${2}")
}
$tokens = $null
$parseErrors = $null
[void][Management.Automation.Language.Parser]::ParseInput($content, [ref]$tokens, [ref]$parseErrors)
if ($parseErrors.Count -gt 0) {
    throw "Staged Install.ps1 has syntax errors: $($parseErrors.Message -join '; ')"
}
[IO.File]::WriteAllText($OutputPath, $content, (New-Object Text.UTF8Encoding($false)))
Write-Host "Staged Install.ps1 for $Repository v$Version."
