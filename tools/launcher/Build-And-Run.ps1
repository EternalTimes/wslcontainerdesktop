<#
.SYNOPSIS
    Builds an x64 MSIX from the current sources, registers its fresh loose layout and launches it.

.DESCRIPTION
    Defaults to Debug, as used by the "WSL Container Desktop (Dev)" shortcut. Each invocation writes
    a new unsigned MSIX and layout under AppPackages\Launcher; signing for distribution remains
    the release workflow's responsibility. -PackageOnly builds and validates without registering
    or launching. -Revision overrides only the fourth package version component in a manifest copy,
    leaving the source manifest unchanged.

.EXAMPLE
    ./tools/launcher/Build-And-Run.ps1 -PackageOnly -Configuration Release -Revision 1
#>
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Debug',

    [ValidateRange(0, 65535)]
    [int]$Revision,

    [switch]$PackageOnly
)

$ErrorActionPreference = 'Stop'

$repoRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..\..')).Path
$projectDir = Join-Path $repoRoot 'src\WslContainerDesktop'
$project = Join-Path $projectDir 'WslContainerDesktop.csproj'
$sourceManifest = Join-Path $projectDir 'Package.appxmanifest'
[xml]$source = Get-Content -LiteralPath $sourceManifest -Raw
$sourceVersion = [version]$source.Package.Identity.Version
if ($sourceVersion.Revision -lt 0 -or
    @($sourceVersion.Major, $sourceVersion.Minor, $sourceVersion.Build, $sourceVersion.Revision).Where({ $_ -gt 65535 }).Count) {
    throw 'The source manifest must have a four-part MSIX version with components from 0 to 65535.'
}
$buildRevision = if ($PSBoundParameters.ContainsKey('Revision')) { $Revision } else { $sourceVersion.Revision }
$packageVersion = '{0}.{1}.{2}.{3}' -f $sourceVersion.Major, $sourceVersion.Minor, $sourceVersion.Build, $buildRevision
$packageName = [string]$source.Package.Identity.Name
$publisher = [string]$source.Package.Identity.Publisher

# An empty output directory makes a successful build's package unambiguous, even after failed runs.
# Keep the layout: Windows' dev registration runs directly from it.
$runDir = Join-Path $repoRoot "AppPackages\Launcher\$Configuration\$packageVersion\$([guid]::NewGuid().ToString('N'))"
$layoutDir = Join-Path $runDir 'layout'
$packageDir = Join-Path $runDir 'package'
New-Item -ItemType Directory -Path $layoutDir, $packageDir -Force | Out-Null
$inputManifest = Join-Path $runDir 'Package.launcher.appxmanifest'
$source.Package.Identity.SetAttribute('Version', $packageVersion)
$source.Save($inputManifest)
$manifest = Join-Path $layoutDir 'AppxManifest.xml'

$dotnet = (Get-Command dotnet -ErrorAction Stop).Source
$buildArgs = @(
    'build', $project, '-c', $Configuration, '-p:Platform=x64', '-p:RuntimeIdentifier=win-x64'
    "-p:Version=$packageVersion", "-p:AssemblyVersion=$packageVersion", "-p:FileVersion=$packageVersion"
    '-p:GenerateAppxPackageOnBuild=true', '-p:AppxPackageSigningEnabled=false'
    '-p:UapAppxPackageBuildMode=SideloadOnly', '-p:AppxBundle=Never'
    # The launcher needs the MSIX, not a separate native symbol archive; keep Debug PDBs in the layout.
    '-p:IncludeDebugSymbolsProjectOutputGroup=false'
    '-p:CopyOutputSymbolsToPublishDirectory=false'
    '-p:SelfContained=true', '-p:WindowsAppSDKSelfContained=true'
    "-p:LauncherAppxManifest=$inputManifest", "-p:OutDir=$layoutDir/", "-p:AppxPackageDir=$packageDir/"
    '-v', 'minimal'
)

Write-Host "Building WSL Container Desktop $packageVersion ($Configuration, x64)..." -ForegroundColor Cyan
# Resolve global.json from this checkout even when invoked through a shortcut or another directory.
Push-Location $repoRoot
try {
    & $dotnet @buildArgs
    $buildExit = $LASTEXITCODE
}
finally { Pop-Location }
if ($buildExit -ne 0) { throw "Build failed (exit code $buildExit)." }
if (-not (Test-Path -LiteralPath $manifest)) { throw "Build did not generate the layout manifest: $manifest" }

function Assert-BuildIdentity([xml]$Document, [string]$Location) {
    if ($Document.Package.Identity.Name -ne $packageName -or
        $Document.Package.Identity.Publisher -ne $publisher -or
        $Document.Package.Identity.Version -ne $packageVersion -or
        $Document.Package.Identity.ProcessorArchitecture -ne 'x64') {
        throw "The package identity/version/architecture at '$Location' does not match this build."
    }
}

[xml]$builtManifest = Get-Content -LiteralPath $manifest -Raw
Assert-BuildIdentity $builtManifest $manifest
$packages = @(Get-ChildItem -LiteralPath $packageDir -Recurse -File -Filter *.msix |
    Where-Object { $_.FullName -notmatch '\\Dependencies\\' })
if ($packages.Count -ne 1) { throw "Expected one newly built MSIX under '$packageDir'; found $($packages.Count)." }
$msix = $packages[0]
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($msix.FullName)
try {
    $entry = $archive.GetEntry('AppxManifest.xml')
    if (-not $entry) { throw 'The MSIX is missing AppxManifest.xml.' }
    $reader = New-Object System.IO.StreamReader($entry.Open())
    try { [xml]$packageManifest = $reader.ReadToEnd() }
    finally { $reader.Dispose() }
    Assert-BuildIdentity $packageManifest $msix.FullName
}
finally { $archive.Dispose() }
Write-Host "Package (unsigned): $($msix.FullName)" -ForegroundColor Green
if ($PackageOnly) { return }

# Registration must succeed before looking up an AUMID; otherwise an old installation could launch.
Add-AppxPackage -Register $manifest -ForceUpdateFromAnyVersion -ForceApplicationShutdown -ErrorAction Stop
$packages = @(Get-AppxPackage -Name $packageName | Where-Object {
    $_.Version -eq $packageVersion -and $_.Publisher -eq $publisher -and
    [System.IO.Path]::GetFullPath($_.InstallLocation).TrimEnd('\') -eq $layoutDir.TrimEnd('\')
})
if ($packages.Count -ne 1) { throw 'Windows did not register this build at its fresh layout location; launch cancelled.' }
$pkg = $packages[0]
$appId = [string]$builtManifest.Package.Applications.Application.Id
if (-not $appId) { throw 'The generated manifest has no application ID to launch.' }
$aumid = "$($pkg.PackageFamilyName)!$appId"
Write-Host "Launching $packageVersion..." -ForegroundColor Green
Start-Process "shell:AppsFolder\$aumid"
