#requires -Version 7.0
<#
.SYNOPSIS
    Imports reviewed screenshots from a GitHub Actions artifact and updates README.
.DESCRIPTION
    Assumes the pbiq-terminal-screenshots artifact was generated from the public
    fixture tapes. Never use private PBIP projects as screenshot sources.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDirectory
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$src = (Resolve-Path -LiteralPath $SourceDirectory).Path
$target = Join-Path $repo 'assets/screenshots'
$readmePath = Join-Path $repo 'README.md'
$names = @(
    'scan-color.png',
    'inspect-measures.png',
    'audit-summary-color.png',
    'audit-warning-color.png'
)

# Find each exact filename once, even if GitHub download preserved folders.
$found = @{}
foreach ($name in $names) {
    $hits = @(Get-ChildItem -LiteralPath $src -Recurse -File | Where-Object Name -EQ $name)
    if ($hits.Count -ne 1) {
        throw "Expected exactly one '$name' in screenshot artifact; found $($hits.Count)."
    }
    $file = $hits[0]
    $bytes = [System.IO.File]::ReadAllBytes($file.FullName)
    $pngMagic = [byte[]](0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a)
    if ($bytes.Length -lt 25000) { throw "Suspiciously small screenshot: $name" }
    for ($i = 0; $i -lt $pngMagic.Length; $i++) {
        if ($bytes[$i] -ne $pngMagic[$i]) { throw "Not a PNG file: $name" }
    }
    $found[$name] = $file.FullName
}

$original = Get-Content -LiteralPath $readmePath -Raw -Encoding utf8
$marker = '(?s)<!-- MEDIA SLOT \(02\.5\.3\.3 / 02\.5\.3\.4\):.*?-->'
$alreadyLinked = $original.Contains('assets/screenshots/scan-color.png')
$mediaMatch = [regex]::Match($original, $marker)
if (-not $mediaMatch.Success -and -not $alreadyLinked) {
    throw 'README media marker not found; refusing to modify an unfamiliar README.'
}

$section = @'
![PBIQ scan of a synthetic PBIP project in a color terminal](assets/screenshots/scan-color.png)

<details>
<summary><strong>More terminal screenshots: inspect and audit</strong></summary>

**Measure metadata (plain text by design)**

![PBIQ inspect listing aggregate measure format and type metadata](assets/screenshots/inspect-measures.png)

**Quality summary with coverage gaps**

![PBIQ audit passing with explicitly reported type-coverage gaps](assets/screenshots/audit-summary-color.png)

**Strict warning policy failure**

![PBIQ audit blocking on a synthetic bidirectional relationship warning](assets/screenshots/audit-warning-color.png)

</details>
'@

New-Item -Path $target -ItemType Directory -Force | Out-Null
foreach ($name in $names) {
    Copy-Item -LiteralPath $found[$name] -Destination (Join-Path $target $name) -Force
}
if (-not $alreadyLinked) {
    $updated = $original.Substring(0, $mediaMatch.Index) + $section +
               $original.Substring($mediaMatch.Index + $mediaMatch.Length)
    [System.IO.File]::WriteAllText($readmePath, $updated, [System.Text.UTF8Encoding]::new($false))
}

Write-Host 'Imported four PNG screenshots from reviewed artifact.'
Write-Host 'README contains image links. Preview them in VS Code/GitHub before committing.'
Write-Host 'Run: git diff --check; git status --short'
