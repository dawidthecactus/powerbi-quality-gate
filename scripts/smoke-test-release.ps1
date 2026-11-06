#Requires -Version 7.0
<#
.SYNOPSIS
  Smoke-test the extracted, standalone PBIQ binary on Windows and Linux.
.DESCRIPTION
  Uses only synthetic public PBIP/Fabric fixtures and validates release
  behavior, stdout/stderr, machine reports, source locations and gate exits.
  This checks the distributed executable, NOT a source-tree dotnet DLL.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [ValidateNotNullOrEmpty()]
    [string]$DistributionDirectory,

    [Parameter(Mandatory = $true)]
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?$')]
    [string]$ExpectedVersion,

    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$distribution = (Resolve-Path -LiteralPath $DistributionDirectory -ErrorAction Stop).Path
$root = (Resolve-Path -LiteralPath $RepositoryRoot -ErrorAction Stop).Path
$executableName = if ($IsWindows) { 'pbiq.exe' } else { 'pbiq' }
$binary = Join-Path $distribution $executableName

foreach ($name in @($executableName, 'README.md', 'LICENSE')) {
    if (-not (Test-Path -LiteralPath (Join-Path $distribution $name) -PathType Leaf)) {
        throw "Distribution is missing required file: $name"
    }
}

$baseline = Join-Path $root 'examples/valid-pbir-tmdl'
$warning = Join-Path $root 'examples/warning-pbir-tmdl'
$fabric = Join-Path $root 'examples/fabric-git/Revenue.SemanticModel'
$fabricWarning = Join-Path $root 'examples/fabric-git-warning/Warning.SemanticModel'
$config = Join-Path $root 'examples/pbiq.example.yml'
foreach ($fixture in @($baseline, $warning, $fabric, $fabricWarning, $config)) {
    if (-not (Test-Path -LiteralPath $fixture)) {
        throw "Required public synthetic fixture was not found: $fixture"
    }
}

$temp = Join-Path ([System.IO.Path]::GetTempPath()) ('pbiq-release-smoke-' + [guid]::NewGuid().ToString('N'))
New-Item -Path $temp -ItemType Directory -Force | Out-Null
try {
    $cases = @(
        @{ Name='version'; Arguments=@('--version'); Expected=0; Marker=$ExpectedVersion; Stream='stdout' }
        @{ Name='scan'; Arguments=@('scan',$baseline,'--no-color'); Expected=0; Marker="POWER BI QUALITY GATE  v$ExpectedVersion"; Stream='stdout' }
        @{ Name='measure inspection'; Arguments=@('inspect',$baseline,'--measures','--no-color'); Expected=0; Marker='Measures inspected: 2/2'; Stream='stdout' }
        @{ Name='audit baseline'; Arguments=@('audit',$baseline,'--quiet'); Expected=0; Marker='PASS WITH COVERAGE GAPS'; Stream='stdout' }
        @{ Name='audit warning policy'; Arguments=@('audit',$warning,'--fail-on-warning','--quiet'); Expected=1; Marker='FAIL (WARNING POLICY)'; Stream='stderr' }
        @{ Name='audit coverage policy'; Arguments=@('audit',$baseline,'--min-measure-coverage','95','--quiet'); Expected=1; Marker='FAIL (COVERAGE POLICY)'; Stream='stderr' }
        @{ Name='invalid output modes'; Arguments=@('audit',$baseline,'--details','--quiet','--no-color'); Expected=2; Marker='INVALID OUTPUT MODE'; Stream='stderr' }
        @{ Name='JSON baseline'; Arguments=@('audit',$baseline,'--format','json'); Expected=0; Marker='"schemaVersion"'; Stream='stdout'; Format='json'; Status='pass_with_coverage_gaps'; Project='Example.pbip' }
        @{ Name='JSON warning policy'; Arguments=@('audit',$warning,'--fail-on-warning','--format','json'); Expected=1; Marker='"fail_warning_policy"'; Stream='stdout'; Format='json'; Status='fail_warning_policy' }
        @{ Name='JSON configured exclusion'; Arguments=@('audit',$baseline,'--config',$config,'--format','json'); Expected=0; Marker='"suppressed"'; Stream='stdout'; Format='json'; Status='pass_with_coverage_gaps'; Suppressed=1 }
        @{ Name='JSON missing model'; Arguments=@('audit',(Join-Path $temp 'missing-project'),'--format','json'); Expected=2; Marker='"not_evaluated"'; Stream='stdout'; Format='json'; Status='not_evaluated' }
        @{ Name='Fabric SARIF baseline'; Arguments=@('audit',$fabric,'--format','sarif','--sarif-source-root',$root); Expected=0; Marker='"2.1.0"'; Stream='stdout'; Format='sarif'; UriPrefix='examples/fabric-git/' }
        @{ Name='Fabric SARIF warning policy'; Arguments=@('audit',$fabricWarning,'--fail-on-warning','--format','sarif','--sarif-source-root',$root); Expected=1; Marker='"2.1.0"'; Stream='stdout'; Format='sarif'; UriPrefix='examples/fabric-git-warning/' }
    )

    foreach ($case in $cases) {
        $stdout = Join-Path $temp 'stdout.txt'
        $stderr = Join-Path $temp 'stderr.txt'
        $quotedArguments = foreach ($argument in $case.Arguments) {
            if ($argument -match '[\s"]') {
                '"' + $argument.Replace('"', '\"') + '"'
            }
            else {
                $argument
            }
        }
        $process = Start-Process -FilePath $binary -ArgumentList ($quotedArguments -join ' ') `
            -Wait -PassThru -NoNewWindow `
            -RedirectStandardOutput $stdout -RedirectStandardError $stderr

        $streamPath = if ($case.Stream -eq 'stderr') { $stderr } else { $stdout }
        $result = [string](Get-Content -LiteralPath $streamPath -Raw -Encoding utf8)
        if ($process.ExitCode -ne $case.Expected -or -not $result.Contains($case.Marker)) {
            Write-Host "FAIL $($case.Name): got exit $($process.ExitCode), expected $($case.Expected)."
            throw "Distribution smoke test failed: $($case.Name)"
        }
        if ($result.Contains([char]27)) {
            throw "Distribution smoke test emitted ANSI control codes: $($case.Name)"
        }

        if ($case.ContainsKey('Format')) {
            if ((Get-Item -LiteralPath $stderr).Length -ne 0) {
                throw "Machine output produced unexpected stderr: $($case.Name)"
            }
            $report = $result | ConvertFrom-Json -Depth 60 -ErrorAction Stop
            if ($case.Format -eq 'json') {
                if ($report.schemaVersion -ne '1.0.0' -or
                    $report.result.exitCode -ne $case.Expected -or
                    $report.result.status -ne $case.Status) {
                    throw "JSON contract mismatch: $($case.Name)"
                }
                if ($case.ContainsKey('Project') -and $report.project.name -ne $case.Project) {
                    throw "JSON project identity mismatch: $($case.Name)"
                }
                if ($case.ContainsKey('Suppressed') -and
                    $report.summary.suppressed -ne $case.Suppressed) {
                    throw "JSON exclusion suppression mismatch: $($case.Name)"
                }
                if ($case.Expected -eq 2 -and $null -eq $report.failure) {
                    throw 'JSON audit failure must not look like a successful evaluation.'
                }
            }
            else {
                if ($report.version -ne '2.1.0' -or @($report.runs).Count -ne 1 -or
                    $report.runs[0].tool.driver.name -ne 'pbiq' -or
                    $report.runs[0].properties.pbiqExitCode -ne $case.Expected) {
                    throw "SARIF contract mismatch: $($case.Name)"
                }
                $uris = @($report.runs[0].results | ForEach-Object {
                    foreach ($location in @($_.locations)) {
                        if ($null -ne $location -and $null -ne $location.physicalLocation) {
                            $location.physicalLocation.artifactLocation.uri
                        }
                    }
                } | Where-Object { $_ -and $_.StartsWith($case.UriPrefix) })
                if ($uris.Count -eq 0) {
                    throw "Fabric SARIF lost verified checkout-relative source locations: $($case.Name)"
                }
            }
        }
        Write-Host "PASS $($case.Name): exit $($process.ExitCode)"
    }
    Write-Host "PASS distribution $ExpectedVersion ($executableName): all $($cases.Count) smoke cases"
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
