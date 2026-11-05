#requires -Version 7.0
<#
.SYNOPSIS
  PBIQ 02.4.1: read-only compatibility check of real local PBIP projects.
.DESCRIPTION
  Finds PBIP, PBIX, PBIT and ZIP candidates under selected roots. Runs PBIQ
  scan, inspect --tom, audit and audit --fail-on-warning on PBIP candidates.
  Private logs remain in Documents, never inside the repository.
  A separate share-summary.csv excludes project paths and model metadata.
.EXAMPLE
  .\scripts\real-world-validation.ps1 -ProjectRoots @('C:\MyProjects\Aviation','C:\MyProjects\Nielsen') -GroupNames @('Aviation','Nielsen') -PathNamePatterns @('.*','(?i)nielsen|fresh|soft')
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string[]] $ProjectRoots,

    [string[]] $GroupNames = @(),

    [string[]] $PathNamePatterns = @(),

    [string] $RepositoryRoot = (Get-Location).Path,

    [string] $OutputRoot = (Join-Path ([Environment]::GetFolderPath('MyDocuments')) 'PBIQ-RealWorld-Audits'),

    [ValidateRange(1, 20)]
    [int] $MaxDepth = 6,

    [ValidateRange(1, 50)]
    [int] $MaxProjectsPerGroup = 10,

    [switch] $DiscoveryOnly,

    [switch] $SkipInspect
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ($GroupNames.Count -gt 0 -and $GroupNames.Count -ne $ProjectRoots.Count) {
    throw '-GroupNames must contain the same number of entries as -ProjectRoots.'
}
if ($PathNamePatterns.Count -gt 0 -and $PathNamePatterns.Count -ne $ProjectRoots.Count) {
    throw '-PathNamePatterns must contain the same number of entries as -ProjectRoots.'
}

$repoRoot = [System.IO.Path]::GetFullPath($RepositoryRoot)
$dll = Join-Path $repoRoot 'src/PowerBIQualityGate.Cli/bin/Release/net10.0/pbiq.dll'
if (-not $DiscoveryOnly -and -not (Test-Path -LiteralPath $dll -PathType Leaf)) {
    throw "PBIQ Release DLL not found: $dll`nRun 'dotnet build -c Release' in the repository first."
}
if (-not $DiscoveryOnly -and -not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'dotnet was not found in PATH.'
}

# Keep reports and possibly sensitive TMDL object names OUTSIDE Git.
[void][System.IO.Directory]::CreateDirectory($OutputRoot)
$runName = 'run-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + ([guid]::NewGuid().ToString('N').Substring(0, 6))
$runDir = Join-Path $OutputRoot $runName
[void][System.IO.Directory]::CreateDirectory($runDir)
$utf8 = [System.Text.UTF8Encoding]::new($false)

function Find-Projects {
    param(
        [string] $Root,
        [string] $NamePattern,
        [int] $DepthLimit
    )

    $paths = [System.Collections.Generic.List[System.IO.FileInfo]]::new()
    $rootPath = [Environment]::ExpandEnvironmentVariables($Root)
    $rootItem = $null
    if (Test-Path -LiteralPath $rootPath) {
        $rootItem = Get-Item -LiteralPath $rootPath -ErrorAction Stop
    }
    else {
        # A user may supply a report stem without its .pbix/.pbip extension.
        foreach ($ext in @('.pbip', '.pbix', '.pbit', '.zip')) {
            if (Test-Path -LiteralPath ($rootPath + $ext) -PathType Leaf) {
                $rootItem = Get-Item -LiteralPath ($rootPath + $ext)
                break
            }
        }
        if ($null -eq $rootItem) {
            return @()
        }
    }

    if (-not $rootItem.PSIsContainer) {
        if ($rootItem.Extension -in @('.pbip', '.pbix', '.pbit', '.zip')) {
            [void]$paths.Add([System.IO.FileInfo]$rootItem)
        }
        return $paths.ToArray()
    }

    $ignoredDirectories = @('.git', 'node_modules', 'bin', 'obj', '.next', '.venv', 'venv',
                            '.pbi', '.pbipcache', 'dist', 'build', '.vs', 'data', 'backups')
    $stack = [System.Collections.Generic.Stack[object]]::new()
    $stack.Push([pscustomobject]@{ Folder = $rootItem.FullName; Depth = 0 })

    while ($stack.Count -gt 0) {
        $entry = $stack.Pop()
        $folder = [string]$entry.Folder
        $depth = [int]$entry.Depth

        foreach ($file in @(Get-ChildItem -LiteralPath $folder -File -ErrorAction SilentlyContinue)) {
            if ($file.Extension -notin @('.pbip', '.pbix', '.pbit', '.zip')) { continue }
            if ($file.FullName -notmatch $NamePattern) { continue }
            [void]$paths.Add([System.IO.FileInfo]$file)
        }
        if ($depth -ge $DepthLimit) { continue }

        foreach ($child in @(Get-ChildItem -LiteralPath $folder -Directory -ErrorAction SilentlyContinue)) {
            if ($child.Name -in $ignoredDirectories) { continue }
            if (($child.Attributes -band [System.IO.FileAttributes]::ReparsePoint) -ne 0) { continue }
            $stack.Push([pscustomobject]@{ Folder = $child.FullName; Depth = $depth + 1 })
        }
    }

    return @($paths.ToArray() | Sort-Object FullName -Unique)
}

function Invoke-Pbiq {
    param(
        [string] $ProjectFile,
        [string[]] $CommandArgs,
        [string] $OutputFile
    )

    $startInfo = [System.Diagnostics.ProcessStartInfo]::new()
    $startInfo.FileName = 'dotnet'
    $startInfo.WorkingDirectory = $repoRoot
    $startInfo.UseShellExecute = $false
    $startInfo.RedirectStandardOutput = $true
    $startInfo.RedirectStandardError = $true
    $startInfo.CreateNoWindow = $true
    $startInfo.StandardOutputEncoding = $utf8
    $startInfo.StandardErrorEncoding = $utf8
    $startInfo.Environment['NO_COLOR'] = '1'
    $startInfo.Environment['DOTNET_NOLOGO'] = '1'
    $startInfo.ArgumentList.Add($dll)
    foreach ($argument in $CommandArgs) {
        $startInfo.ArgumentList.Add($argument)
    }
    $startInfo.ArgumentList.Add($ProjectFile)
    $startInfo.ArgumentList.Add('--no-color')

    $process = [System.Diagnostics.Process]::new()
    $process.StartInfo = $startInfo
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    try {
        if (-not $process.Start()) { throw 'Unable to start dotnet process.' }
        # Read both streams asynchronously to avoid deadlocks on large models.
        $stdoutTask = $process.StandardOutput.ReadToEndAsync()
        $stderrTask = $process.StandardError.ReadToEndAsync()
        $process.WaitForExit()
        $stdout = $stdoutTask.GetAwaiter().GetResult()
        $stderr = $stderrTask.GetAwaiter().GetResult()
        $watch.Stop()
        $content = "COMMAND: pbiq $($CommandArgs -join ' ') <private PBIP path> --no-color`r`n"
        $content += "EXIT CODE: $($process.ExitCode)`r`nDURATION_MS: $($watch.ElapsedMilliseconds)`r`n`r`n"
        $content += "STDOUT:`r`n$stdout`r`nSTDERR:`r`n$stderr"
        [System.IO.File]::WriteAllText($OutputFile, $content, $utf8)
        return [pscustomobject]@{ ExitCode = [int]$process.ExitCode; Text = ($stdout + "`n" + $stderr); Milliseconds = $watch.ElapsedMilliseconds }
    }
    finally {
        $process.Dispose()
    }
}

function Get-AuditCounts {
    param([string] $Text)
    $match = [regex]::Match($Text,
        '(?m)^\s*(?<rules>\d+)\s+rules evaluated\s*\|\s*(?<errors>\d+)\s+errors?\s*\|\s*(?<warnings>\d+)\s+warnings?\s*\|\s*(?<infos>\d+)\s+information')
    if (-not $match.Success) {
        return [pscustomobject]@{ Rules = ''; Errors = ''; Warnings = ''; Information = '' }
    }
    return [pscustomobject]@{
        Rules = $match.Groups['rules'].Value
        Errors = $match.Groups['errors'].Value
        Warnings = $match.Groups['warnings'].Value
        Information = $match.Groups['infos'].Value
    }
}

function Get-ModelCounts {
    param([string] $Text)
    $objects = [regex]::Match($Text,
        '(?m)^\s*(?<tables>\d+)\s+tables\s+(?<columns>\d+)\s+columns\s+(?<measures>\d+)\s+measures')
    $relations = [regex]::Match($Text,
        '(?m)^\s*(?<relationships>\d+)\s+relationships')
    return [pscustomobject]@{
        Tables = if ($objects.Success) { $objects.Groups['tables'].Value } else { '-' }
        Columns = if ($objects.Success) { $objects.Groups['columns'].Value } else { '-' }
        Measures = if ($objects.Success) { $objects.Groups['measures'].Value } else { '-' }
        Relationships = if ($relations.Success) { $relations.Groups['relationships'].Value } else { '-' }
    }
}

function New-Row {
    param([string] $Group, [string] $Alias, [string] $FullPath, [string] $Kind)
    return [pscustomobject]@{
        Group = $Group
        Alias = $Alias
        Kind = $Kind
        FullPath = $FullPath
        Scan = '-'
        TOM = '-'
        Tables = '-'
        Columns = '-'
        Measures = '-'
        Relationships = '-'
        Audit = '-'
        Strict = '-'
        Rules = '-'
        Errors = '-'
        Warnings = '-'
        Information = '-'
        Note = ''
    }
}

$rows = [System.Collections.Generic.List[object]]::new()
$groups = [System.Collections.Generic.List[object]]::new()

for ($index = 0; $index -lt $ProjectRoots.Count; $index++) {
    $group = if ($GroupNames.Count -gt 0) { $GroupNames[$index] } else { 'Group' + ($index + 1) }
    $pattern = if ($PathNamePatterns.Count -gt 0) { $PathNamePatterns[$index] } else { '.*' }
    $null = [regex]::new($pattern) # validate supplied filter

    $files = @(Find-Projects -Root $ProjectRoots[$index] -NamePattern $pattern -DepthLimit $MaxDepth)
    # Prefer PBIP when both editable project and PBIX archive are present.
    $selected = @($files | Sort-Object @{ Expression = { if ($_.Extension -ieq '.pbip') { 0 } elseif ($_.Extension -ieq '.pbix') { 1 } else { 2 } } }, FullName | Select-Object -First $MaxProjectsPerGroup)
    [void]$groups.Add([pscustomobject]@{ Group = $group; CandidatesFound = $files.Count; Selected = $selected.Count })
    Write-Host "[$group] candidates: $($files.Count), selected: $($selected.Count)"

    if ($selected.Count -eq 0) {
        $row = New-Row -Group $group -Alias ($group + '-00') -FullPath '' -Kind 'NOT_FOUND'
        $row.Note = 'No PBIP/PBIX/PBIT/ZIP found under supplied root; check path or increase -MaxDepth.'
        [void]$rows.Add($row)
        continue
    }

    for ($n = 0; $n -lt $selected.Count; $n++) {
        $file = $selected[$n]
        $alias = ('{0}-{1:00}' -f $group, ($n + 1))
        $row = New-Row -Group $group -Alias $alias -FullPath $file.FullName -Kind $file.Extension.ToUpperInvariant()
        $localOutput = Join-Path $runDir $alias
        [void][System.IO.Directory]::CreateDirectory($localOutput)
        Write-Host ("  {0}: {1}" -f $alias, $file.Extension.ToUpperInvariant())

        if ($file.Extension -ine '.pbip') {
            $row.Note = switch ($file.Extension.ToLowerInvariant()) {
                '.zip' { 'Archive: extract a safe local copy before testing.' }
                '.pbix' { 'PBIX requires File > Save as > Power BI Project (PBIP) in Desktop.' }
                '.pbit' { 'PBIT requires opening in Desktop and saving a PBIP copy.' }
                default { 'Unsupported input format.' }
            }
            [void]$rows.Add($row)
            continue
        }
        if ($DiscoveryOnly) {
            $row.Note = 'Discovered PBIP; no commands executed (-DiscoveryOnly).'
            [void]$rows.Add($row)
            continue
        }

        try {
            $scan = Invoke-Pbiq -ProjectFile $file.FullName -CommandArgs @('scan') -OutputFile (Join-Path $localOutput 'scan.txt')
            $row.Scan = [string]$scan.ExitCode
            if ($scan.ExitCode -ne 0) {
                $row.Note = 'Structural scan did not pass; inspect private scan.txt.'
                [void]$rows.Add($row)
                continue
            }

            if (-not $SkipInspect) {
                $inspect = Invoke-Pbiq -ProjectFile $file.FullName -CommandArgs @('inspect', '--tom') -OutputFile (Join-Path $localOutput 'inspect-tom.txt')
                $row.TOM = [string]$inspect.ExitCode
                if ($inspect.ExitCode -ne 0) {
                    $row.Note = 'TOM inspection failed; inspect private inspect-tom.txt.'
                    [void]$rows.Add($row)
                    continue
                }
                $modelCounts = Get-ModelCounts $inspect.Text
                $row.Tables = $modelCounts.Tables
                $row.Columns = $modelCounts.Columns
                $row.Measures = $modelCounts.Measures
                $row.Relationships = $modelCounts.Relationships
            }

            $audit = Invoke-Pbiq -ProjectFile $file.FullName -CommandArgs @('audit') -OutputFile (Join-Path $localOutput 'audit-default.txt')
            $strict = Invoke-Pbiq -ProjectFile $file.FullName -CommandArgs @('audit', '--fail-on-warning') -OutputFile (Join-Path $localOutput 'audit-strict.txt')
            $row.Audit = [string]$audit.ExitCode
            $row.Strict = [string]$strict.ExitCode
            $counts = Get-AuditCounts $audit.Text
            $row.Rules = $counts.Rules
            $row.Errors = $counts.Errors
            $row.Warnings = $counts.Warnings
            $row.Information = $counts.Information
            $row.Note = if ($audit.ExitCode -eq 0 -and $strict.ExitCode -eq 1) {
                'Default PASS, strict warning gate FAIL (expected if warning > 0).'
            } elseif ($audit.ExitCode -eq 0 -and $strict.ExitCode -eq 0) {
                'Both audit policies PASS.'
            } elseif ($audit.ExitCode -eq 2) {
                'Audit unable to run; inspect private logs.'
            } else {
                'Review private audit logs for details.'
            }
        }
        catch {
            # A broken project must not terminate the rest of the batch.
            $row.Note = 'Runner error; see private runner-error.txt.'
            [System.IO.File]::WriteAllText((Join-Path $localOutput 'runner-error.txt'), $_.Exception.ToString(), $utf8)
        }
        [void]$rows.Add($row)
    }
}

$privatePath = Join-Path $runDir 'private-results.csv'
$sharePath = Join-Path $runDir 'share-summary.csv'
$readmePath = Join-Path $runDir 'README-PRIVATE.txt'
$rows.ToArray() | Export-Csv -LiteralPath $privatePath -NoTypeInformation -Encoding utf8
$rows.ToArray() | Select-Object Group, Alias, Kind, Scan, TOM, Tables, Columns, Measures, Relationships, Audit, Strict, Rules, Errors, Warnings, Information, Note |
    Export-Csv -LiteralPath $sharePath -NoTypeInformation -Encoding utf8
$readme = @"
PBIQ 02.4.1: REAL-WORLD VALIDATION

Only share-summary.csv is designed for easy sharing. It has aliases instead of
local paths, model object names and DAX expressions. Check it before sharing.

PRIVATE: private-results.csv and subfolder *.txt logs can include model names,
column names, relationship names, DAX previews, filesystem paths or error
messages. Never commit or publish these logs without reviewing/redacting them.

Input projects were inspected read-only. No Power BI Desktop connections,
refreshes, writes or automatic PBIX conversions were performed.

To widen discovery, pass a more specific -ProjectRoots path or increase
-MaxDepth / -MaxProjectsPerGroup. Git itself is not modified.
"@
[System.IO.File]::WriteAllText($readmePath, $readme, $utf8)

Write-Host ''
Write-Host '=== PBIQ REAL-WORLD RESULTS (aliases only) ==='
$rows.ToArray() | Select-Object Group, Alias, Kind, Scan, TOM, Tables, Measures, Relationships, Audit, Strict, Warnings, Information, Note | Format-Table -AutoSize
Write-Host "Shareable summary: $sharePath"
Write-Host "Private detailed logs: $runDir"
Write-Host 'NOTE: This runner does not interpret a project-specific FAIL as a runner crash.'
