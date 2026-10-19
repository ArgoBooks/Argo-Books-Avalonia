<#
.SYNOPSIS
    Builds the Windows release with Velopack: publishes, packs, and code-signs it with Azure Trusted Signing.

.DESCRIPTION
    Writes the release to publish\velopack\win and the download to publish\ArgoBooks-<version>-win-Setup.exe. Signing needs the Azure sign-in from docs/setup/OneTimeReleaseSetup.md.

.PARAMETER Sandbox
    Builds the Debug configuration, which checks the sandbox update folder and trusts the sandbox
    signing key. Use it for testing an update on dev.argorobots.com, never for a real release.

.PARAMETER SkipCodeSigning
    Leaves the files without an Authenticode signature, for a local test only. Windows warns about
    an unsigned Setup.exe, and the release check fails on it.

.EXAMPLE
    powershell -File packaging\windows\build-release.ps1
#>
[CmdletBinding()]
param(
    [switch]$Sandbox,
    [switch]$SkipCodeSigning
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).ProviderPath

function Fail([string]$message) {
    Write-Host ''
    Write-Host "ERROR: $message" -ForegroundColor Red
    exit 1
}

$props = [xml](Get-Content -Raw (Join-Path $root 'Directory.Build.props'))
$version = ($props.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if (-not $version) { Fail 'Could not read <Version> from Directory.Build.props.' }

# The packaging tool must match the Velopack library the app ships with, or the updater it puts in the package may not understand the app.
$packages = [xml](Get-Content -Raw (Join-Path $root 'Directory.Packages.props'))
$libraryVersion = ($packages.Project.ItemGroup.PackageVersion | Where-Object { $_.Include -eq 'Velopack' }).Version
$vpk = Get-Command 'vpk' -ErrorAction SilentlyContinue
if (-not $vpk) { Fail "vpk is not on PATH. Install it with: dotnet tool install --global vpk --version $libraryVersion" }
$vpkHelp = (& vpk --skip-updates -h 2>&1 | Out-String)
$vpkVersion = if ($vpkHelp -match 'Velopack CLI (\S+?),') { $Matches[1] } else { 'unknown' }
if ($vpkVersion -ne $libraryVersion) { Fail "vpk is $vpkVersion but the app uses Velopack $libraryVersion. Run: dotnet tool update --global vpk --version $libraryVersion" }

$configuration = if ($Sandbox) { 'Debug' } else { 'Release' }
$publishDir = Join-Path $root 'publish\win-x64'
$outputDir = Join-Path $root 'publish\velopack\win'

Write-Host "Building Argo Books $version for Windows ($configuration)..."

if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
# Publish rather than build: PublishReadyToRun only applies to a publish, and it saves about three seconds on every cold start.
& dotnet publish (Join-Path $root 'ArgoBooks.Desktop') -c $configuration -f net10.0-windows10.0.17763.0 -r win-x64 --self-contained -o $publishDir
if ($LASTEXITCODE -ne 0) { Fail 'dotnet publish failed.' }

if (Test-Path $outputDir) { Remove-Item -Recurse -Force $outputDir }

$packArgs = @(
    'pack',
    '--packId', 'ArgoBooksApp',
    '--packVersion', $version,
    '--packDir', $publishDir,
    '--mainExe', 'Argo Books.exe',
    '--packTitle', 'Argo Books',
    '--packAuthors', 'Argo Books',
    '--icon', (Join-Path $root 'ArgoBooks\Assets\argo-logo.ico'),
    '--channel', 'win',
    # Only whole packages carry a signature the app checks, so partial updates are never built.
    '--delta', 'None',
    '--outputDir', $outputDir
)
if (-not $SkipCodeSigning) {
    $packArgs += @('--azureTrustedSignFile', (Join-Path $PSScriptRoot 'trusted-signing-metadata.json'))
}

& vpk @packArgs
if ($LASTEXITCODE -ne 0) { Fail 'vpk pack failed.' }

$setup = Join-Path $outputDir 'ArgoBooksApp-win-Setup.exe'
$download = Join-Path $root "publish\ArgoBooks-$version-win-Setup.exe"
Copy-Item $setup $download -Force

Write-Host ''
Write-Host "Built Argo Books $version for Windows." -ForegroundColor Green
Write-Host "  Update files:   $outputDir"
Write-Host "  Download file:  $download"
if ($SkipCodeSigning) { Write-Host '  NOT code-signed: for a local test only.' -ForegroundColor Yellow }
Write-Host ''
Write-Host 'Next: sign the release for the app with packaging\sign-release.ps1 (docs/Publishing.md, "Going Live").'
