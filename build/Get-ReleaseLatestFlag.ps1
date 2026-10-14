# Copyright (C) 2026 Michael Hacker
# SPDX-License-Identifier: GPL-3.0-or-later
# This program is free software: you can redistribute it and/or modify it under the
# terms of the GNU General Public License as published by the Free Software Foundation,
# either version 3 of the License, or (at your option) any later version.
# This program is distributed without any warranty; without even the implied warranty
# of MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See <https://www.gnu.org/licenses/>.
<#
.SYNOPSIS
    Determines whether a stable release may replace the current latest stable release.
#>
param(
    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,
    [AllowEmptyString()]
    [string]$LatestTag
)
$ErrorActionPreference = 'Stop'
if (-not $LatestTag) { return '--latest' }
if ($LatestTag -notmatch '^v(\d+\.\d+\.\d+)$') {
    throw "Cannot compare latest stable release tag '$LatestTag'. Refusing to change latest."
}
if ([version]$Version -gt [version]$Matches[1]) { return '--latest' }
return '--latest=false'
