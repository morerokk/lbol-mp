#Requires -Version 5.1
<#
.SYNOPSIS
    Keeps the version in sync across all files that need the mod version.

.DESCRIPTION
    LBOLMP.csproj holds the real version. This copies it into the following three files:

      LBOLMP/MpInfo.cs      the Version const, which is what [BepInPlugin] reads
      LBOLMP/manifest.json  version_number, for Thunderstore
      LBOLMP/modinfo.json   Version, for the in-game mod loader

    Each file is edited in place with a regex rather than being reformatted, so the
    modinfo changelog, the escapes inside it, the byte order marks and the line
    endings remain untouched.

    The build runs this itself, so to bump the version, bump it in the csproj.

.PARAMETER Version
    A new version to set, e.g. 0.12.0. Writes it to the csproj first, then syncs
    everything to it. Omit to just sync to whatever the csproj already says.

.EXAMPLE
    .\tools\Sync-Version.ps1
    Copy the csproj's version into the other three files.

.EXAMPLE
    .\tools\Sync-Version.ps1 -Version 0.12.0
    Bump everything, csproj included, to 0.12.0.
#>
[CmdletBinding()]
param(
    [string]$Version
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $root 'LBOLMP\LBOLMP.csproj'

# Read as bytes so the byte order mark can be put back exactly as it was found.
function Read-SourceFile([string]$Path) {
    if (-not (Test-Path -LiteralPath $Path)) {
        throw "Cannot find $Path."
    }

    $bytes = [System.IO.File]::ReadAllBytes($Path)
    $bom = $bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF
    $offset = if ($bom) { 3 } else { 0 }

    [PSCustomObject]@{
        Path = $Path
        Bom  = $bom
        Text = [System.Text.Encoding]::UTF8.GetString($bytes, $offset, $bytes.Length - $offset)
    }
}

function Save-SourceFile($File, [string]$Text) {
    $encoding = New-Object System.Text.UTF8Encoding($File.Bom)
    [System.IO.File]::WriteAllText($File.Path, $Text, $encoding)
}

# Replaces only what the two capture groups surround. Returns the label if it had to,
# and nothing if the file already agreed, so the caller can report the set in one line.
function Set-VersionField([string]$Path, [string]$Pattern, [string]$Value, [string]$Label) {
    $file = Read-SourceFile $Path

    if ($file.Text -notmatch $Pattern) {
        throw "$Label has no version field matching /$Pattern/. Has the file changed shape?"
    }

    $updated = [regex]::Replace($file.Text, $Pattern, ('${1}' + $Value + '${2}'))
    if ($updated -eq $file.Text) {
        return
    }

    Save-SourceFile $file $updated
    return $Label
}

$csproj = Read-SourceFile $project
if ($csproj.Text -notmatch '<Version>([^<]*)</Version>') {
    throw "No <Version> element in $project."
}

$current = $Matches[1]
if (-not $Version) {
    $Version = $current
}

# Thunderstore only accepts major.minor.patch, and a bad one is not worth finding out
# about at upload time.
if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "'$Version' is not a major.minor.patch version."
}

$changed = @()

if ($Version -ne $current) {
    $changed += Set-VersionField $project '(<Version>)[^<]*(</Version>)' $Version 'LBOLMP.csproj'
}

# Anchored on "public const string Version" so that ProtocolVersion is left alone.
$changed += Set-VersionField (Join-Path $root 'LBOLMP\MpInfo.cs') `
    '(public const string Version = ")[^"]*(")' $Version 'MpInfo.cs'

# Both keys are matched only at the start of a line, so a changelog entry that happens
# to quote one can never be rewritten.
$changed += Set-VersionField (Join-Path $root 'LBOLMP\manifest.json') `
    '(?m)^(\s*"version_number"\s*:\s*")[^"]*(")' $Version 'manifest.json'

$changed += Set-VersionField (Join-Path $root 'LBOLMP\modinfo.json') `
    '(?m)^(\s*"Version"\s*:\s*")[^"]*(")' $Version 'modinfo.json'

# Write-Output rather than Write-Host, so this shows up in the build log too.
$changed = @($changed | Where-Object { $_ })
if ($changed.Count -gt 0) {
    Write-Output "Version set to $Version in $($changed -join ', ')."
}
