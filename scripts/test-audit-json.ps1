#Requires -Version 7.0
<#
.SYNOPSIS
  Runs real (not mocked) audit --format json CLI exit-code smoke cases.
.DESCRIPTION
  Uses synthetic PBIP fixtures only. JSON is always emitted to stdout,
  including for gate failures and discovery errors.
#>
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dll = Join-Path $root 'src/PowerBIQualityGate.Cli/bin/Release/net10.0/pbiq.dll'
if (-not (Test-Path -LiteralPath $dll)) { throw "Build PBIQ in Release mode first: $dll" }

$temp = Join-Path ([IO.Path]::GetTempPath()) ('pbiq-json-contract-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp -Force | Out-Null
try {
    $cases = @(
        @{ Name = 'pass'; Args = @('audit','examples/valid-pbir-tmdl','--format','json'); Code = 0; Status = 'pass_with_coverage_gaps' }
        @{ Name = 'warning'; Args = @('audit','examples/warning-pbir-tmdl','--fail-on-warning','--format','json'); Code = 1; Status = 'fail_warning_policy' }
        @{ Name = 'coverage'; Args = @('audit','examples/valid-pbir-tmdl','--min-measure-coverage','95','--format','json'); Code = 1; Status = 'fail_coverage_policy' }
        @{ Name = 'missing'; Args = @('audit','examples/no-such-fixture','--format','json'); Code = 2; Status = 'not_evaluated' }
        @{ Name = 'conflicting modes'; Args = @('audit','examples/valid-pbir-tmdl','--quiet','--format','json'); Code = 2; Status = 'not_evaluated' }
        @{ Name = 'bad coverage'; Args = @('audit','examples/valid-pbir-tmdl','--min-measure-coverage','101','--format','json'); Code = 2; Status = 'not_evaluated' }
    )
    Push-Location -LiteralPath $root
    try {
        foreach ($case in $cases) {
            $stdout = Join-Path $temp 'stdout.json'
            $stderr = Join-Path $temp 'stderr.txt'
            $process = Start-Process -FilePath 'dotnet' -ArgumentList (@($dll) + $case.Args) `
                -Wait -PassThru -NoNewWindow -RedirectStandardOutput $stdout -RedirectStandardError $stderr
            $raw = Get-Content -LiteralPath $stdout -Raw -Encoding utf8
            $json = $raw | ConvertFrom-Json -ErrorAction Stop
            $err = Get-Content -LiteralPath $stderr -Raw -Encoding utf8
            if ($process.ExitCode -ne $case.Code -or $json.result.exitCode -ne $case.Code -or
                $json.result.status -ne $case.Status -or $json.schemaVersion -ne '1.0.0' -or
                $json.command -ne 'audit') {
                throw "JSON CLI contract mismatch: $($case.Name)"
            }
            if (-not [string]::IsNullOrWhiteSpace($err) -or $raw.Contains([char]27)) {
                throw "Unexpected stderr/ANSI output: $($case.Name)"
            }
            if ($case.Code -eq 0 -and $json.summary.rulesEvaluated -ne 4) {
                throw "Expected 4 rules in success report"
            }
            Write-Host "PASS JSON $($case.Name): exit $($case.Code)"
        }
    }
    finally { Pop-Location }
}
finally { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue }
