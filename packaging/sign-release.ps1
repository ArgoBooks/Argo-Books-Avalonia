<#
.SYNOPSIS
    Signs a Velopack release for the app's updater and stages its small files in the website repo.

.DESCRIPTION
    Writes <file>.sig beside each update package and the Windows Setup.exe, then copies the update lists, signatures and release.json into the website repo. It never uploads, commits or pushes.

.PARAMETER ReleaseDir
    Folder holding one subfolder per channel (win, osx-arm64, osx-x64, linux), as the build scripts
    write them. Defaults to publish\velopack.

.PARAMETER DownloadsDir
    Folder holding the four files the website offers for download (ArgoBooks-<version>-win-Setup.exe,
    both macOS zips and the AppImage). Defaults to the folder above ReleaseDir, which is publish\.

.PARAMETER Website
    The website repo. Falls back to the ARGO_WEBSITE environment variable.

.PARAMETER Sandbox
    Stages the release in the sandbox update folder instead. Needs -KeyPath, since a sandbox build
    trusts the sandbox key.

.PARAMETER KeyPath
    Folder holding NetSparkle_Ed25519.priv and .pub. Leave it out for a real release, which uses the
    production key in %LOCALAPPDATA%\netsparkle.

.EXAMPLE
    powershell -File packaging\sign-release.ps1 -Website "C:\laragon\www\argo-books-website"
#>
[CmdletBinding()]
param(
    [string]$ReleaseDir,
    [string]$DownloadsDir,
    [string]$Website,
    [switch]$Sandbox,
    [string]$KeyPath
)

$ErrorActionPreference = 'Stop'

function Fail([string]$message) {
    Write-Host ''
    Write-Host "ERROR: $message" -ForegroundColor Red
    exit 1
}

if (-not $ReleaseDir) { $ReleaseDir = Join-Path $PSScriptRoot '..\publish\velopack' }
if (-not (Test-Path -LiteralPath $ReleaseDir -PathType Container)) { Fail "Release folder not found: $ReleaseDir" }
$releaseFull = (Resolve-Path -LiteralPath $ReleaseDir).ProviderPath
if (-not $DownloadsDir) { $DownloadsDir = Split-Path -Parent $releaseFull }

if (-not $Website) { $Website = $env:ARGO_WEBSITE }
if (-not $Website) { Fail 'No website repo given. Pass -Website, or set the ARGO_WEBSITE environment variable to the website repo folder.' }
if (-not (Test-Path -LiteralPath (Join-Path $Website 'get_avalonia_installer.php'))) { Fail "$Website is not the website repo." }
if ($Sandbox -and -not $KeyPath) { Fail 'A sandbox release has to be signed with the sandbox key. Pass its folder with -KeyPath.' }
$updatesDir = Join-Path $Website ('resources\' + $(if ($Sandbox) { 'updates-sandbox' } else { 'updates' }))

$tool = Get-Command 'netsparkle-generate-appcast' -ErrorAction SilentlyContinue
if (-not $tool) { Fail 'netsparkle-generate-appcast is not on PATH. Install it with: dotnet tool install --global NetSparkleUpdater.Tools.AppCastGenerator' }
$keyArgs = if ($KeyPath) { @('--key-path', $KeyPath) } else { @() }

$packages = @(Get-ChildItem -LiteralPath $releaseFull -Recurse -File -Filter '*-full.nupkg')
if ($packages.Count -eq 0) { Fail "No *-full.nupkg under $releaseFull. Build the release first." }

# A stale package from an earlier build signs cleanly and would then go live as the newest update, so every package must claim the same version.
$versions = @($packages | ForEach-Object {
    if ($_.Name -notmatch '^ArgoBooksApp-(\d+\.\d+\.\d+)(-[a-z0-9-]+)?-full\.nupkg$') { Fail "$($_.FullName) is not named the way the build scripts name packages." }
    $Matches[1]
} | Select-Object -Unique)
if ($versions.Count -gt 1) { Fail ('The release mixes versions ({0}). Remove the stale packages and run again.' -f ($versions -join ', ')) }
$version = $versions[0]

$feeds = @()
$toSign = @($packages)
foreach ($package in $packages) {
    $feed = Get-ChildItem -LiteralPath $package.DirectoryName -File -Filter 'releases.*.json' | Select-Object -First 1
    if (-not $feed) { Fail "No releases.<channel>.json beside $($package.Name)." }
    $listed = (Get-Content -Raw -LiteralPath $feed.FullName | ConvertFrom-Json).Assets | Where-Object { $_.FileName -eq $package.Name }
    if (-not $listed) { Fail "$($feed.Name) does not list $($package.Name)." }
    if ([int64]$listed.Size -ne $package.Length) { Fail "$($feed.Name) gives $($package.Name) a size of $($listed.Size), but the file is $($package.Length) bytes." }
    $feeds += $feed

    $setup = Join-Path $package.DirectoryName 'ArgoBooksApp-win-Setup.exe'
    if (Test-Path -LiteralPath $setup) { $toSign += Get-Item -LiteralPath $setup }
}

$channels = @($packages | ForEach-Object { Split-Path -Leaf $_.DirectoryName })
$missingChannels = @('win', 'osx-arm64', 'osx-x64', 'linux' | Where-Object { $channels -notcontains $_ })

$downloadNames = @("ArgoBooks-$version-win-Setup.exe", "ArgoBooks-$version-osx-arm64.zip", "ArgoBooks-$version-osx-x64.zip", "ArgoBooks-$version-linux-x64.AppImage")
$downloads = [ordered]@{}
$missingDownloads = @()
foreach ($name in $downloadNames) {
    $path = Join-Path $DownloadsDir $name
    if (Test-Path -LiteralPath $path) { $downloads[$name] = (Get-Item -LiteralPath $path).Length } else { $missingDownloads += $name }
}

# The website offers a download once it reaches the size in release.json, so a download missing from it would be offered while still uploading.
if (-not $Sandbox) {
    if ($missingChannels.Count -gt 0) { Fail ('Not in this release: {0}. Add them to {1} and run this again.' -f ($missingChannels -join ', '), $releaseFull) }
    if ($missingDownloads.Count -gt 0) { Fail ('Downloads not found in {0}: {1}' -f $DownloadsDir, ($missingDownloads -join ', ')) }
}

Write-Host "Signing Argo Books $version in $releaseFull"
Write-Host ''

foreach ($file in $toSign) {
    $output = (& $tool.Source --generate-signature $file.FullName @keyArgs | Out-String)
    if ($output -notmatch 'Signature:\s*(\S+)') { Fail ('Could not read a signature for {0}. The tool printed:{1}{2}' -f $file.Name, [Environment]::NewLine, $output.Trim()) }
    $signature = $Matches[1]

    # The tool exits 0 whether a signature is valid or not, so its text is the only signal.
    $check = (& $tool.Source --verify $file.FullName --signature $signature @keyArgs | Out-String)
    if ($check -notmatch 'Signature valid') { Fail ('The signature just made for {0} does not verify. The tool printed:{1}{2}' -f $file.Name, [Environment]::NewLine, $check.Trim()) }

    # No trailing newline, so the file holds exactly the signature the app reads.
    [System.IO.File]::WriteAllText($file.FullName + '.sig', $signature, (New-Object System.Text.UTF8Encoding($false)))
    Write-Host ('  {0,-48} {1,8:N1} MB, built {2}, signed' -f $file.Name, ($file.Length / 1MB), $file.LastWriteTime.ToString('yyyy-MM-dd HH:mm'))
}

# ---------------------------------------------------------------- stage the small files in the website repo

New-Item -ItemType Directory -Force -Path $updatesDir | Out-Null
foreach ($feed in $feeds) { Copy-Item -LiteralPath $feed.FullName -Destination $updatesDir -Force }
foreach ($file in $toSign) { Copy-Item -LiteralPath ($file.FullName + '.sig') -Destination $updatesDir -Force }

# Only the real release.json drives the website's download page, so a sandbox release has none.
if (-not $Sandbox) {
    $release = [ordered]@{
        version   = $version
        date      = (Get-Date).ToString('yyyy-MM-dd')
        downloads = $downloads
    }
    [System.IO.File]::WriteAllText((Join-Path $updatesDir 'release.json'), ($release | ConvertTo-Json -Depth 3), (New-Object System.Text.UTF8Encoding($false)))
}

# ---------------------------------------------------------------- summary

Write-Host ''
Write-Host "Signed $($toSign.Count) file(s) for $version and staged the update lists in $updatesDir." -ForegroundColor Green
if ($missingChannels.Count -gt 0) { Write-Host ('Not in this release: {0}.' -f ($missingChannels -join ', ')) -ForegroundColor Yellow }
Write-Host 'Check those build times are all from this release.'
Write-Host ''
Write-Host "Upload with FileZilla, before pushing the website repo:"
Write-Host ("  to resources/{0}/:" -f $(if ($Sandbox) { 'updates-sandbox' } else { 'updates' }))
foreach ($file in $toSign) { Write-Host "    $($file.FullName)" }
if (-not $Sandbox) {
    Write-Host "  to resources/downloads/$version/:"
    foreach ($name in $downloads.Keys) { Write-Host "    $(Join-Path $DownloadsDir $name)" }
}
