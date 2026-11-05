#requires -Version 7.0
<#
.SYNOPSIS
Measure end-to-end local PBIQ audit times without exporting PBIP metadata.
.DESCRIPTION
Runs the compiled pbiq.dll, includes CLI startup, TMDL deserialization and
quality evaluation. One warm-up by default. Only aliases, timings and exit
codes are written to CSV outside the repository.
.EXAMPLE
.\scripts\benchmark-real-world.ps1 -ProjectPaths $paths -Aliases @('Aviation','NC','Nielsen') -Iterations 5
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]] $ProjectPaths,

    [string[]] $Aliases,

    [ValidateRange(1, 25)]
    [int] $Iterations = 5,

    [ValidateRange(0, 5)]
    [int] $Warmups = 1,

    [switch] $FailOnWarning,

    [string] $OutputDirectory = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'PBIQ-RealWorld-Audits')
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$PSNativeCommandUseErrorActionPreference = $false

$repoRoot = Split-Path -Parent $PSScriptRoot
$dll = Join-Path $repoRoot 'src/PowerBIQualityGate.Cli/bin/Release/net10.0/pbiq.dll'
if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) {
    throw 'Compiled pbiq.dll not found. Run dotnet build -c Release first.'
}

if (-not $Aliases -or $Aliases.Count -eq 0) {
    $Aliases = @(for ($i = 0; $i -lt $ProjectPaths.Count; $i++) { 'Model-{0:D2}' -f ($i + 1) })
}
if ($Aliases.Count -ne $ProjectPaths.Count) {
    throw 'Aliases and ProjectPaths must have the same number of entries.'
}
if ($Aliases | Where-Object { $_ -notmatch '^[A-Za-z0-9_-]{1,48}$' }) {
    throw 'Aliases must be short identifiers (letters, digits, underscore or hyphen only).'
}
if (($Aliases | Select-Object -Unique).Count -ne $Aliases.Count) {
    throw 'Each alias must be unique.'
}

$runId = 'benchmark-{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), ([guid]::NewGuid().ToString('N').Substring(0, 6))
$folder = Join-Path $OutputDirectory $runId
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$rows = [System.Collections.Generic.List[object]]::new()

for ($i = 0; $i -lt $ProjectPaths.Count; $i++) {
    $path = $ProjectPaths[$i]
    $alias = $Aliases[$i]
    if (-not (Test-Path -LiteralPath $path)) {
        throw "Input not found for alias '$alias'."
    }

    $times = [System.Collections.Generic.List[double]]::new()
    $codes = [System.Collections.Generic.HashSet[int]]::new()
    for ($round = -$Warmups; $round -lt $Iterations; $round++) {
        $arguments = @($dll, 'audit', $path, '--no-color')
        if ($FailOnWarning) {
            $arguments += '--fail-on-warning'
        }

        $watch = [System.Diagnostics.Stopwatch]::StartNew()
        & dotnet @arguments 1> $null 2> $null
        $exitCode = $LASTEXITCODE
        $watch.Stop()

        if ($exitCode -notin @(0, 1)) {
            throw "Audit of '$alias' returned $exitCode. Benchmark requires an evaluated audit (0 or 1)."
        }
        [void] $codes.Add($exitCode)
        if ($round -ge 0) {
            $times.Add($watch.Elapsed.TotalMilliseconds)
        }
    }

    $sorted = @($times | Sort-Object)
    $medianIndex = [int][math]::Floor(($sorted.Count - 1) / 2)
    $median = if ($sorted.Count % 2 -eq 0) {
        ($sorted[$medianIndex] + $sorted[$medianIndex + 1]) / 2.0
    }
    else {
        $sorted[$medianIndex]
    }
    $p95Index = [int][math]::Ceiling(0.95 * $sorted.Count) - 1

    $rows.Add([pscustomobject]@{
        Alias = $alias
        Iterations = $Iterations
        MedianMs = [math]::Round($median, 2)
        P95Ms = [math]::Round($sorted[$p95Index], 2)
        MinMs = [math]::Round($sorted[0], 2)
        MaxMs = [math]::Round($sorted[-1], 2)
        ExitCodes = ($codes | Sort-Object) -join '/'
        Policy = if ($FailOnWarning) { 'strict' } else { 'default' }
    })
    Write-Host ('{0}: median {1:N1} ms, p95 {2:N1} ms, exits {3}' -f $alias, $median, $sorted[$p95Index], ($codes -join '/'))
}

$csv = Join-Path $folder 'benchmark-summary.csv'
$rows | Export-Csv -LiteralPath $csv -NoTypeInformation -Encoding utf8
Write-Host "Benchmark summary: $csv"
Write-Host 'Only aliases, aggregate timings and exit codes were stored. No DAX or model metadata was exported.'
$rows | Format-Table -AutoSize
