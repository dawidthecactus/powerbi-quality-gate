#requires -Version 7.0
<#
.SYNOPSIS
  Offline, read-only PBIQ 02.4.5.5 measure-quality validation and benchmark.
.DESCRIPTION
  Runs inspect --measures, audit --no-color and audit --quiet on selected
  local PBIP projects. Validates arithmetic and cross-command consistency,
  checks deterministic aggregates and reports median/p95 end-to-end timings.
  Raw CLI output is kept IN MEMORY ONLY. The CSV contains aliases, aggregate
  numbers, timing and generic result codes, never names, paths or DAX.
.EXAMPLE
  .\scripts\validate-measure-quality.ps1 -ProjectPaths @($aviation,$nc,$nielsen) -Aliases @('Aviation','NC','Nielsen')
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]] $ProjectPaths,

    [string[]] $Aliases = @(),

    [ValidateRange(1, 25)]
    [int] $Iterations = 3,

    [ValidateRange(0, 5)]
    [int] $Warmups = 1,

    [ValidateRange(5, 600)]
    [int] $TimeoutSeconds = 120,

    [string] $OutputDirectory = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($ProjectPaths.Count -eq 0) { throw 'Provide at least one local PBIP path.' }
if ($Aliases.Count -eq 0) {
    $Aliases = @(for ($i = 0; $i -lt $ProjectPaths.Count; $i++) { 'Model-{0:D2}' -f ($i + 1) })
}
if ($Aliases.Count -ne $ProjectPaths.Count) { throw 'Alias count must equal project count.' }
foreach ($alias in $Aliases) {
    if ($alias -cnotmatch '^[A-Za-z0-9_-]{1,48}$') { throw 'Aliases must be unique, short alphanumeric identifiers.' }
}
if (@($Aliases | Sort-Object -Unique).Count -ne $Aliases.Count) { throw 'Aliases must be unique.' }

$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$dll = Join-Path $repoRoot 'src/PowerBIQualityGate.Cli/bin/Release/net10.0/pbiq.dll'
if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) { throw 'Release CLI missing. Run dotnet build -c Release.' }
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) { throw '.NET runtime not found.' }

if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $documents = [Environment]::GetFolderPath('MyDocuments')
    if ([string]::IsNullOrWhiteSpace($documents)) { $documents = [IO.Path]::GetTempPath() }
    $OutputDirectory = Join-Path $documents 'PBIQ-RealWorld-Audits'
}
$outputRoot = [IO.Path]::GetFullPath($OutputDirectory)
$comparison = if ($IsWindows) { [StringComparison]::OrdinalIgnoreCase } else { [StringComparison]::Ordinal }
$repoWithSeparator = $repoRoot.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
if ($outputRoot.Equals($repoRoot, $comparison) -or $outputRoot.StartsWith($repoWithSeparator, $comparison)) {
    throw 'Output directory must be outside the repository.'
}

$projectFiles = [System.Collections.Generic.List[string]]::new()
foreach ($path in $ProjectPaths) {
    $file = [IO.Path]::GetFullPath($path)
    if (-not (Test-Path -LiteralPath $file -PathType Leaf) -or [IO.Path]::GetExtension($file) -ine '.pbip') {
        throw 'One project input does not exist or is not a PBIP file.'
    }
    $inputFolder = [IO.Path]::GetDirectoryName($file)
    if ($outputRoot.Equals($inputFolder, $comparison) -or
        $outputRoot.StartsWith($inputFolder.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, $comparison)) {
        throw 'Output directory must be outside every input project folder.'
    }
    $projectFiles.Add($file)
}

# ProcessStartInfo treats expected exit code 1/2 as DATA, not PowerShell errors.
# No raw CLI text is written to disk or the terminal, even on failure.
function Invoke-PbiqSafely {
    param([string[]] $CommandArguments)

    $info = [Diagnostics.ProcessStartInfo]::new()
    $info.FileName = 'dotnet'
    $info.WorkingDirectory = $repoRoot
    $info.UseShellExecute = $false
    $info.CreateNoWindow = $true
    $info.RedirectStandardOutput = $true
    $info.RedirectStandardError = $true
    $info.StandardOutputEncoding = [Text.UTF8Encoding]::new($false)
    $info.StandardErrorEncoding = [Text.UTF8Encoding]::new($false)
    $info.Environment['NO_COLOR'] = '1'
    $info.Environment['DOTNET_NOLOGO'] = '1'
    [void]$info.ArgumentList.Add($dll)
    foreach ($arg in $CommandArguments) { [void]$info.ArgumentList.Add($arg) }

    $process = [Diagnostics.Process]::new()
    $process.StartInfo = $info
    $watch = [Diagnostics.Stopwatch]::StartNew()
    try {
        if (-not $process.Start()) { throw 'PROCESS_START_FAILED' }
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit($TimeoutSeconds * 1000)) {
            $process.Kill($true)
            $process.WaitForExit()
            throw 'PROCESS_TIMEOUT'
        }
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        $watch.Stop()
        $combined = if ([string]::IsNullOrEmpty($stdout)) { $stderr.Trim() } else { ($stdout + "`n" + $stderr).Trim() }
        return [pscustomobject]@{
            Code = [int]$process.ExitCode
            Text = $combined
            Milliseconds = [double]$watch.Elapsed.TotalMilliseconds
        }
    }
    finally { $process.Dispose() }
}

function Get-RequiredMatch {
    param([string] $Text, [string] $Expression, [string] $FailureCode)
    $match = [regex]::Match($Text, $Expression)
    if (-not $match.Success) { throw $FailureCode }
    return $match
}

function Get-Number {
    param([System.Text.RegularExpressions.Match] $Match, [string] $Name)
    return [int]::Parse($Match.Groups[$Name].Value, [Globalization.CultureInfo]::InvariantCulture)
}

function Parse-Inspection {
    param([string] $Text)
    $a = Get-RequiredMatch $Text '(?m)^Measures inspected: (?<inspected>\d+)/(?<total>\d+)\s+\(visible (?<visible>\d+), hidden (?<hidden>\d+)\)' 'INSPECT_TOTAL_NOT_FOUND'
    $b = Get-RequiredMatch $Text '(?m)^Format \(visible\): static (?<static>\d+)\s+\|\s+dynamic (?<dynamic>\d+)\s+\|\s+missing (?<missing>\d+)' 'INSPECT_FORMAT_NOT_FOUND'
    $c = Get-RequiredMatch $Text '(?m)^Type \(visible\): numeric (?<numeric>\d+)\s+\|\s+nonnumeric (?<nonnumeric>\d+)\s+\|\s+unknown (?<unknown>\d+)\s+\|\s+unsupported (?<unsupported>\d+)' 'INSPECT_TYPE_NOT_FOUND'
    $d = Get-RequiredMatch $Text '(?m)^Metadata notes: blank static (?<blankstatic>\d+)\s+\|\s+blank dynamic expression (?<blankdynamic>\d+)\s+\|\s+unavailable dynamic expression (?<unavailable>\d+)\s+\|\s+both format kinds (?<both>\d+)' 'INSPECT_NOTES_NOT_FOUND'
    $values = [ordered]@{}
    foreach ($key in @('inspected','total','visible','hidden')) { $values[$key] = Get-Number $a $key }
    foreach ($key in @('static','dynamic','missing')) { $values[$key] = Get-Number $b $key }
    foreach ($key in @('numeric','nonnumeric','unknown','unsupported')) { $values[$key] = Get-Number $c $key }
    foreach ($key in @('blankstatic','blankdynamic','unavailable','both')) { $values[$key] = Get-Number $d $key }
    return [pscustomobject]$values
}

function Parse-Audit {
    param([string] $Text)
    # Core audit fields are ASCII. Decorative separators and verdict symbols
    # are NOT a stable machine interface: redirecting .NET CLI output on some
    # Windows code pages can transcode Unicode bullets and middle dots.
    # Anchor on stable labels/numbers instead, without weakening validation.
    $a = Get-RequiredMatch $Text '(?m)^[ \t]*(?<tables>\d+)[ \t]+tables[^\r\n\d]+(?<measures>\d+)[ \t]+measures[^\r\n\d]+(?<relationships>\d+)[ \t]+relationships' 'AUDIT_PROFILE_NOT_FOUND'
    $b = Get-RequiredMatch $Text '(?m)^[ \t]*MEASURE QUALITY[^\r\n\d]+(?<inspected>\d+)/(?<total>\d+) inspected \((?<visible>\d+) visible\)' 'AUDIT_QUALITY_NOT_FOUND'
    $c = Get-RequiredMatch $Text '(?m)^[ \t]*Formats:[ \t]*(?<static>\d+)[ \t]+static[^\r\n\d]+(?<dynamic>\d+)[ \t]+dynamic[^\r\n\d]+(?<missing>\d+)[ \t]+missing[^\r\n]*?SM-002:[ \t]*(?<warnings>\d+)[ \t]+warnings[^\r\n\d]+(?<review>\d+)[ \t]+review' 'AUDIT_FORMAT_NOT_FOUND'
    $d = Get-RequiredMatch $Text '(?m)^[^\r\n]*?AUDIT:[ \t]*(?<status>PASS WITH COVERAGE GAPS|PASS WITH ADVISORIES|PASS|FAIL \(COVERAGE POLICY\)|FAIL \(WARNING POLICY\)|FAIL|NOT EVALUATED)[^\r\n]*?exit code:[ \t]*(?<code>\d+)' 'AUDIT_VERDICT_NOT_FOUND'
    # CompactAuditRenderer uses singular at 1 (e.g. '1 warning'), plural otherwise.
    $e = Get-RequiredMatch $Text '(?m)^[ \t]*(?<errors>\d+)[ \t]+errors?[^\r\n\d]+(?<allwarnings>\d+)[ \t]+warnings?[^\r\n\d]+(?<information>\d+)[ \t]+information[^\r\n\d]+(?<rules>\d+)[ \t]+rules?' 'AUDIT_COUNTS_NOT_FOUND'
    $values = [ordered]@{}
    foreach ($key in @('tables','measures','relationships')) { $values[$key] = Get-Number $a $key }
    foreach ($key in @('inspected','total','visible')) { $values[$key] = Get-Number $b $key }
    foreach ($key in @('static','dynamic','missing','warnings','review')) { $values[$key] = Get-Number $c $key }
    $values['status'] = $d.Groups['status'].Value.Trim()
    $values['code'] = Get-Number $d 'code'
    foreach ($key in @('errors','allwarnings','information','rules')) { $values[$key] = Get-Number $e $key }
    return [pscustomobject]$values
}

function Parse-Quiet {
    param([string] $Text)
    $m = Get-RequiredMatch $Text '\APBIQ \| AUDIT: (?<status>[^|\r\n]+) \| (?<errors>\d+) errors, (?<warnings>\d+) warnings, (?<information>\d+) information \| exit code: (?<code>\d+)\r?\n?\z' 'QUIET_CONTRACT_INVALID'
    return [pscustomobject]@{
        status = $m.Groups['status'].Value.Trim()
        errors = Get-Number $m 'errors'
        warnings = Get-Number $m 'warnings'
        information = Get-Number $m 'information'
        code = Get-Number $m 'code'
    }
}

function Assert-Consistent {
    param($Inspection, $Audit, $Quiet)
    if ($Inspection.inspected -ne $Inspection.total -or
        $Inspection.visible + $Inspection.hidden -ne $Inspection.total -or
        $Inspection.static + $Inspection.dynamic + $Inspection.missing -ne $Inspection.visible -or
        $Inspection.numeric + $Inspection.nonnumeric + $Inspection.unknown + $Inspection.unsupported -ne $Inspection.visible) {
        throw 'INVENTORY_PARTITION_INVALID'
    }
    if ($Inspection.blankstatic -gt ($Inspection.missing + $Inspection.dynamic) -or
        ($Inspection.blankdynamic + $Inspection.unavailable) -gt $Inspection.dynamic -or
        $Inspection.both -gt $Inspection.dynamic) { throw 'INVENTORY_NOTES_INVALID' }
    foreach ($key in @('total','visible','static','dynamic','missing')) {
        if ($Inspection.$key -ne $Audit.$key) { throw 'CROSS_COMMAND_COUNT_MISMATCH' }
    }
    if ($Audit.measures -ne $Inspection.total -or $Audit.inspected -ne $Inspection.total -or
        $Audit.warnings -gt $Inspection.missing -or
        $Audit.review -gt ($Inspection.missing + $Inspection.dynamic) -or
        $Audit.warnings -gt $Audit.allwarnings -or $Audit.rules -ne 4) {
        throw 'AUDIT_AGGREGATES_INVALID'
    }
    if ($Audit.status -ne $Quiet.status -or $Audit.code -ne $Quiet.code -or
        $Audit.errors -ne $Quiet.errors -or $Audit.allwarnings -ne $Quiet.warnings -or
        $Audit.information -ne $Quiet.information) {
        throw 'AUDIT_QUIET_MISMATCH'
    }
}

function Get-Statistics {
    param([double[]] $Samples)
    $sorted = @($Samples | Sort-Object)
    $middle = [int][math]::Floor(($sorted.Count - 1) / 2)
    $median = if ($sorted.Count % 2 -eq 0) { ($sorted[$middle] + $sorted[$middle + 1]) / 2.0 } else { $sorted[$middle] }
    $p95Index = [int][math]::Ceiling(0.95 * $sorted.Count) - 1
    $p95 = $sorted[$p95Index]
    return [pscustomobject]@{ Median = [math]::Round($median, 2); P95 = [math]::Round($p95, 2) }
}

[void][IO.Directory]::CreateDirectory($outputRoot)
$runId = 'measure-validation-{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), ([guid]::NewGuid().ToString('N').Substring(0, 6))
$folder = Join-Path $outputRoot $runId
[void][IO.Directory]::CreateDirectory($folder)
$rows = [System.Collections.Generic.List[object]]::new()
$failed = 0

for ($index = 0; $index -lt $projectFiles.Count; $index++) {
    $alias = $Aliases[$index]
    $path = $projectFiles[$index]
    $inspectionTimings = [System.Collections.Generic.List[double]]::new()
    $auditTimings = [System.Collections.Generic.List[double]]::new()
    $values = @{
        Alias = $alias; Result = 'PASS'; Reason = 'OK'; Iterations = $Iterations
        Tables = ''; Measures = ''; Visible = ''; Hidden = ''; Static = ''; Dynamic = ''; Missing = ''
        Numeric = ''; NonNumeric = ''; Unknown = ''; Unsupported = ''; ConfirmedWarnings = ''; NeedsReview = ''
        AuditExit = ''; AuditStatus = ''; InspectMedianMs = ''; InspectP95Ms = ''; AuditMedianMs = ''; AuditP95Ms = ''
    }
    try {
        $baselineInspection = $null
        $baselineAudit = $null
        $baselineQuiet = $null
        for ($round = -$Warmups; $round -lt $Iterations; $round++) {
            $inspect = Invoke-PbiqSafely @('inspect', $path, '--measures', '--no-color')
            if ($inspect.Code -ne 0 -or $inspect.Text.Contains([char]27)) { throw 'INSPECT_FAILED' }
            $inspection = Parse-Inspection $inspect.Text

            $audit = Invoke-PbiqSafely @('audit', $path, '--no-color')
            if ($audit.Code -notin @(0, 1) -or $audit.Text.Contains([char]27)) { throw 'AUDIT_FAILED' }
            $parsedAudit = Parse-Audit $audit.Text
            if ($parsedAudit.code -ne $audit.Code) { throw 'AUDIT_EXIT_MISMATCH' }

            $quiet = Invoke-PbiqSafely @('audit', $path, '--quiet')
            if ($quiet.Code -ne $audit.Code -or $quiet.Text.Contains([char]27)) { throw 'QUIET_EXIT_MISMATCH' }
            $parsedQuiet = Parse-Quiet $quiet.Text
            Assert-Consistent $inspection $parsedAudit $parsedQuiet

            if ($null -ne $baselineInspection) {
                if (($inspection | ConvertTo-Json -Compress) -cne ($baselineInspection | ConvertTo-Json -Compress) -or
                    ($parsedAudit | ConvertTo-Json -Compress) -cne ($baselineAudit | ConvertTo-Json -Compress) -or
                    ($parsedQuiet | ConvertTo-Json -Compress) -cne ($baselineQuiet | ConvertTo-Json -Compress)) {
                    throw 'NONDETERMINISTIC_AGGREGATES'
                }
            }
            else {
                $baselineInspection = $inspection
                $baselineAudit = $parsedAudit
                $baselineQuiet = $parsedQuiet
            }
            if ($round -ge 0) {
                $inspectionTimings.Add($inspect.Milliseconds)
                $auditTimings.Add($audit.Milliseconds)
            }
        }
        $inspectStats = Get-Statistics $inspectionTimings.ToArray()
        $auditStats = Get-Statistics $auditTimings.ToArray()
        $values.Tables = $baselineAudit.tables
        $values.Measures = $baselineInspection.total
        $values.Visible = $baselineInspection.visible
        $values.Hidden = $baselineInspection.hidden
        $values.Static = $baselineInspection.static
        $values.Dynamic = $baselineInspection.dynamic
        $values.Missing = $baselineInspection.missing
        $values.Numeric = $baselineInspection.numeric
        $values.NonNumeric = $baselineInspection.nonnumeric
        $values.Unknown = $baselineInspection.unknown
        $values.Unsupported = $baselineInspection.unsupported
        $values.ConfirmedWarnings = $baselineAudit.warnings
        $values.NeedsReview = $baselineAudit.review
        $values.AuditExit = $baselineAudit.code
        $values.AuditStatus = $baselineAudit.status
        $values.InspectMedianMs = $inspectStats.Median
        $values.InspectP95Ms = $inspectStats.P95
        $values.AuditMedianMs = $auditStats.Median
        $values.AuditP95Ms = $auditStats.P95
        Write-Host ('{0}: PASS | {1} measures | {2} review | audit median {3:N1} ms' -f $alias, $values.Measures, $values.NeedsReview, $values.AuditMedianMs)
    }
    catch {
        $failed++
        # Never serialize Exception.ToString(): it can contain private paths,
        # TOM object names, or original DAX. Only a predefined code is shared.
        $allowed = @('PROCESS_TIMEOUT','PROCESS_START_FAILED','INSPECT_FAILED','AUDIT_FAILED',
            'QUIET_EXIT_MISMATCH','AUDIT_EXIT_MISMATCH','INSPECT_TOTAL_NOT_FOUND','INSPECT_FORMAT_NOT_FOUND',
            'INSPECT_TYPE_NOT_FOUND','INSPECT_NOTES_NOT_FOUND','AUDIT_PROFILE_NOT_FOUND',
            'AUDIT_QUALITY_NOT_FOUND','AUDIT_FORMAT_NOT_FOUND','AUDIT_VERDICT_NOT_FOUND',
            'AUDIT_COUNTS_NOT_FOUND','QUIET_CONTRACT_INVALID','INVENTORY_PARTITION_INVALID',
            'INVENTORY_NOTES_INVALID','CROSS_COMMAND_COUNT_MISMATCH','AUDIT_AGGREGATES_INVALID',
            'AUDIT_QUIET_MISMATCH','NONDETERMINISTIC_AGGREGATES')
        $reason = $_.Exception.Message
        if ($reason -cnotin $allowed) { $reason = 'RUNNER_ERROR' }
        $values.Result = 'FAIL'
        $values.Reason = $reason
        Write-Host ('{0}: FAIL | {1}' -f $alias, $reason)
    }
    $rows.Add([pscustomobject]$values)
}

$csv = Join-Path $folder 'share-summary.csv'
# Fixed safe column order, no project paths, CLI output, expressions or object names.
$columns = @('Alias','Result','Reason','Iterations','Tables','Measures','Visible','Hidden',
    'Static','Dynamic','Missing','Numeric','NonNumeric','Unknown','Unsupported',
    'ConfirmedWarnings','NeedsReview','AuditExit','AuditStatus','InspectMedianMs',
    'InspectP95Ms','AuditMedianMs','AuditP95Ms')
$rows | Select-Object -Property $columns | Export-Csv -LiteralPath $csv -Encoding utf8 -NoTypeInformation
Write-Host "Shareable aggregates (review before publishing): $csv"
Write-Host 'Raw project paths, model names, object names and DAX are never written to the summary.'
if ($failed -gt 0) { throw "$failed model(s) failed aggregate validation. Review aliases and reason codes in the summary." }
