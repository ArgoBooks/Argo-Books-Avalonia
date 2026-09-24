<#
.SYNOPSIS
    Signs the four Argo Books release files and writes the signatures into the website appcast.

.DESCRIPTION
    Replaces steps 1 and 2 of "Sign the Release Files" in docs/Publishing.md. It finds the
    installer, AppImage and both macOS zips in a release folder, signs each with the NetSparkle
    Ed25519 key, and writes each signature onto the matching <enclosure> in avalonia-update.xml,
    along with the real file size, the release version and today's date.

    It never commits, pushes or uploads. Review the diff yourself afterwards.

.PARAMETER ReleaseDir
    Folder holding the four built release files.

.PARAMETER Appcast
    Path to the website repo's avalonia-update.xml. Falls back to the ARGO_APPCAST
    environment variable when omitted.

.PARAMETER Version
    Optional. Asserts the files are this version instead of taking the version from
    their filenames.

.EXAMPLE
    powershell -File packaging\sign-release.ps1 "C:\releases\2.0.16" "C:\site\avalonia-update.xml"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$ReleaseDir,

    [Parameter(Position = 1)]
    [string]$Appcast,

    [string]$Version
)

$ErrorActionPreference = 'Stop'

function Fail([string]$message) {
    Write-Host ''
    Write-Host "ERROR: $message" -ForegroundColor Red
    exit 1
}

# Os matches the sparkle:os attribute on the enclosure each file belongs to. Getting this
# mapping wrong by hand is the mistake the script exists to prevent: a Mac rejects an update
# signed for the other architecture, and the XML looks perfectly fine either way.
$platforms = @(
    @{ Os = 'windows';     Label = 'Windows installer';   Pattern = 'Argo Books Installer V.*.exe';   Regex = '^Argo Books Installer V\.(\d+\.\d+\.\d+)\.exe$' }
    @{ Os = 'linux';       Label = 'Linux AppImage';      Pattern = 'ArgoBooks-*-linux-x64.AppImage'; Regex = '^ArgoBooks-(\d+\.\d+\.\d+)-linux-x64\.AppImage$' }
    @{ Os = 'macos-arm64'; Label = 'macOS Apple Silicon'; Pattern = 'ArgoBooks-*-osx-arm64.zip';      Regex = '^ArgoBooks-(\d+\.\d+\.\d+)-osx-arm64\.zip$' }
    @{ Os = 'macos-x64';   Label = 'macOS Intel';         Pattern = 'ArgoBooks-*-osx-x64.zip';        Regex = '^ArgoBooks-(\d+\.\d+\.\d+)-osx-x64\.zip$' }
)

# ---------------------------------------------------------------- resolve inputs

if (-not (Test-Path -LiteralPath $ReleaseDir -PathType Container)) {
    Fail "Release folder not found: $ReleaseDir"
}
$releaseFull = (Resolve-Path -LiteralPath $ReleaseDir).ProviderPath

if (-not $Appcast) { $Appcast = $env:ARGO_APPCAST }
if (-not $Appcast) {
    Fail 'No appcast path given. Pass it as the second argument, or set the ARGO_APPCAST environment variable to the full path of avalonia-update.xml.'
}
if (-not (Test-Path -LiteralPath $Appcast -PathType Leaf)) {
    Fail "Appcast not found: $Appcast"
}
$appcastFull = (Resolve-Path -LiteralPath $Appcast).ProviderPath
$text = [System.IO.File]::ReadAllText($appcastFull)

# The sandbox appcast is signed with a different key pair, the one Debug builds trust.
# Writing production signatures into it would break sandbox updates without any error.
if ($text -match '<title>[^<]*sandbox[^<]*</title>') {
    Fail "$appcastFull is the sandbox appcast, which is signed with the sandbox key. This script signs with the production key. Point it at avalonia-update.xml instead."
}

$tool = Get-Command 'netsparkle-generate-appcast' -ErrorAction SilentlyContinue
if (-not $tool) {
    Fail 'netsparkle-generate-appcast is not on PATH. Install it with: dotnet tool install --global NetSparkleUpdater.Tools.AppCastGenerator'
}
$toolPath = $tool.Source

Write-Host "Release folder: $releaseFull"
Write-Host "Appcast:        $appcastFull"
Write-Host ''

# ---------------------------------------------------------------- find the files

$files = @{}
$foundVersions = @()
$missingFiles = @()

foreach ($p in $platforms) {
    $hits = @(Get-ChildItem -LiteralPath $releaseFull -Filter $p.Pattern -File -ErrorAction SilentlyContinue)

    if ($hits.Count -eq 0) {
        $missingFiles += ('  {0,-22} no file matching {1}' -f $p.Label, $p.Pattern)
        continue
    }
    if ($hits.Count -gt 1) {
        $names = ($hits | ForEach-Object { '  ' + $_.Name }) -join [Environment]::NewLine
        Fail ('{0}: {1} files match {2}. Leave exactly one in the folder:{3}{4}' -f $p.Label, $hits.Count, $p.Pattern, [Environment]::NewLine, $names)
    }

    $file = $hits[0]
    if ($file.Name -notmatch $p.Regex) {
        Fail ('{0}: "{1}" is not named the way get_avalonia_installer.php and the appcast URLs expect.' -f $p.Label, $file.Name)
    }

    $files[$p.Os] = $file
    $foundVersions += $Matches[1]
}

if ($missingFiles.Count -gt 0) {
    Fail ('These release files are missing from the folder, so nothing was signed:{0}{1}' -f [Environment]::NewLine, ($missingFiles -join [Environment]::NewLine))
}

# A stale file left over from an earlier release is the failure that would otherwise sail
# through: it signs cleanly, and the appcast then points a live download at old bytes.
$distinctVersions = @($foundVersions | Select-Object -Unique)
if ($distinctVersions.Count -gt 1) {
    $detail = @()
    foreach ($p in $platforms) { $detail += ('  {0,-22} {1}' -f $p.Label, $files[$p.Os].Name) }
    Fail ('The folder mixes versions ({0}). One of these files is left over from an earlier build:{1}{2}' -f ($distinctVersions -join ', '), [Environment]::NewLine, ($detail -join [Environment]::NewLine))
}

$newVersion = $distinctVersions[0]
if ($Version -and $Version -ne $newVersion) {
    Fail "You asked for version $Version but the files in the folder are $newVersion."
}

# ---------------------------------------------------------------- sign and verify

$signatures = @{}

foreach ($p in $platforms) {
    $file = $files[$p.Os]
    Write-Host ('Signing {0,-22} {1}' -f $p.Label, $file.Name)

    $output = (& $toolPath --generate-signature $file.FullName | Out-String)
    if ($output -notmatch 'Signature:\s*(\S+)') {
        Fail ('Could not read a signature for {0}. The tool printed:{1}{2}' -f $file.Name, [Environment]::NewLine, $output.Trim())
    }
    $signature = $Matches[1]

    # The tool exits 0 whether a signature is valid or not, so its text is the only signal.
    $check = (& $toolPath --verify $file.FullName --signature $signature | Out-String)
    if ($check -notmatch 'Signature valid') {
        Fail ('The signature just generated for {0} does not verify against the file. The tool printed:{1}{2}' -f $file.Name, [Environment]::NewLine, $check.Trim())
    }

    $signatures[$p.Os] = $signature
}

Write-Host ''

# ---------------------------------------------------------------- edit the appcast

$versionTags = [regex]::Matches($text, '<sparkle:version>([^<]+)</sparkle:version>')
if ($versionTags.Count -eq 0) {
    Fail "No <sparkle:version> element in $appcastFull. Is that the right file?"
}
$oldVersions = @($versionTags | ForEach-Object { $_.Groups[1].Value } | Select-Object -Unique)
if ($oldVersions.Count -gt 1) {
    Fail ('The appcast already lists more than one version ({0}), so a version replace would be ambiguous. Fix it by hand.' -f ($oldVersions -join ', '))
}
$oldVersion = $oldVersions[0]

# Replace-all of the old version, the same edit the release doc describes doing by hand.
# The boundaries exclude digits only, never dots: the Windows URL spells the version
# "V.2.0.16.exe", so a dot-excluding boundary would skip it and leave the URL pointing at
# the previous release while every other field moved on.
$versionPattern = '(?<!\d)' + [regex]::Escape($oldVersion) + '(?!\d)'
$expectedVersionCount = [regex]::Matches($text, [regex]::Escape($oldVersion)).Count

if ($oldVersion -ne $newVersion) {
    $text = [regex]::Replace($text, $versionPattern, $newVersion)
}

$pubDate = [DateTime]::UtcNow.ToString('ddd, dd MMM yyyy HH:mm:ss +0000', [System.Globalization.CultureInfo]::InvariantCulture)

$edits = @()
$seen = @{}

foreach ($item in [regex]::Matches($text, '(?s)<item>.*?</item>')) {
    $block = $item.Value

    if ($block -notmatch 'sparkle:os="([^"]*)"') {
        Write-Warning 'Skipped an <item> whose enclosure has no sparkle:os attribute.'
        continue
    }
    $os = $Matches[1]

    if (-not $signatures.ContainsKey($os)) {
        Write-Warning "Skipped an <item> for an unrecognised platform: sparkle:os=$os."
        continue
    }
    if ($seen.ContainsKey($os)) {
        Fail "The appcast has two <item> entries for sparkle:os=$os. Remove one before signing."
    }
    $seen[$os] = $true

    $file = $files[$os]
    $block = [regex]::Replace($block, 'sparkle:edSignature="[^"]*"', 'sparkle:edSignature="' + $signatures[$os] + '"')
    $block = [regex]::Replace($block, '\blength="[^"]*"', 'length="' + $file.Length + '"')
    $block = [regex]::Replace($block, '<pubDate>[^<]*</pubDate>', '<pubDate>' + $pubDate + '</pubDate>')

    $edits += @{ Start = $item.Index; Length = $item.Length; Text = $block }
}

$absent = @($platforms | Where-Object { -not $seen.ContainsKey($_.Os) })
if ($absent.Count -gt 0) {
    Fail ('The appcast has no <item> for: {0}. Nothing was written.' -f (($absent | ForEach-Object { $_.Os }) -join ', '))
}

# Apply back to front so each match index still points at the right place.
for ($i = $edits.Count - 1; $i -ge 0; $i--) {
    $e = $edits[$i]
    $text = $text.Substring(0, $e.Start) + $e.Text + $text.Substring($e.Start + $e.Length)
}

# UTF-8 with no BOM, matching how the file already sits on disk.
[System.IO.File]::WriteAllText($appcastFull, $text, (New-Object System.Text.UTF8Encoding($false)))

# ---------------------------------------------------------------- confirm what landed

$written = [System.IO.File]::ReadAllText($appcastFull)

foreach ($item in [regex]::Matches($written, '(?s)<item>.*?</item>')) {
    $block = $item.Value
    if ($block -notmatch 'sparkle:os="([^"]*)"') { continue }
    $os = $Matches[1]
    if (-not $signatures.ContainsKey($os)) { continue }

    if ($block -notmatch 'sparkle:edSignature="([^"]*)"') {
        Fail "The $os enclosure has no sparkle:edSignature after the edit. Check the file before committing."
    }
    if ($Matches[1] -ne $signatures[$os]) {
        Fail "The $os enclosure does not hold the signature generated for it. Check the file before committing."
    }
}

# Counting occurrences rather than re-running the replace pattern: a pattern that missed a
# spot would also miss it here, which is how a stale version in the Windows URL got through
# once already.
$actualVersionCount = [regex]::Matches($written, [regex]::Escape($newVersion)).Count
if ($actualVersionCount -ne $expectedVersionCount) {
    Fail "The appcast mentioned $oldVersion $expectedVersionCount times but now mentions $newVersion only $actualVersionCount times, so at least one spot was missed. Check the file before committing."
}

# ---------------------------------------------------------------- summary

Write-Host ''
if ($oldVersion -eq $newVersion) {
    Write-Host "Signed $newVersion and updated the appcast (it was already on $newVersion)." -ForegroundColor Green
} else {
    Write-Host "Signed $newVersion and updated the appcast ($oldVersion to $newVersion)." -ForegroundColor Green
}
Write-Host ''

foreach ($p in $platforms) {
    $file = $files[$p.Os]
    Write-Host ('  {0,-22} {1}' -f $p.Label, $file.Name)
    Write-Host ('  {0,-22} {1:N1} MB, built {2}, signature verified' -f '', ($file.Length / 1MB), $file.LastWriteTime.ToString('yyyy-MM-dd HH:mm'))
    Write-Host ''
}

Write-Host 'Check those build times are all from this release, then review the diff:'
Write-Host ('  git -C "{0}" diff -- "{1}"' -f (Split-Path -Parent $appcastFull), (Split-Path -Leaf $appcastFull))
