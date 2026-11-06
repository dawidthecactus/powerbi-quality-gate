#Requires -Version 7.0
<# SARIF 2.1.0 smoke tests against the published CLI entry point. #>
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dll = Join-Path $root 'src/PowerBIQualityGate.Cli/bin/Release/net10.0/pbiq.dll'
if (-not (Test-Path -LiteralPath $dll)) { throw "Build Release first: $dll" }

$temp = Join-Path ([IO.Path]::GetTempPath()) ('pbiq-sarif-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp | Out-Null
try {
    $cases = @(
        @{ Name = 'pass'; Args = @('audit','examples/valid-pbir-tmdl','--format','sarif','--sarif-source-root','.'); Code = 0; Results = 3 }
        @{ Name = 'warning'; Args = @('audit','examples/warning-pbir-tmdl','--fail-on-warning','--format','sarif'); Code = 1; Results = 4 }
        @{ Name = 'missing'; Args = @('audit','examples/not-a-fixture','--format','sarif'); Code = 2; Results = 0 }
        @{ Name = 'bad root'; Args = @('audit','examples/valid-pbir-tmdl','--format','sarif','--sarif-source-root','examples/warning-pbir-tmdl'); Code = 2; Results = 0 }
    )
    Push-Location -LiteralPath $root
    try {
        foreach ($case in $cases) {
            $stdout = Join-Path $temp 'stdout.sarif'
            $stderr = Join-Path $temp 'stderr.txt'
            $process = Start-Process -FilePath 'dotnet' -ArgumentList (@($dll) + $case.Args) `
                -Wait -PassThru -NoNewWindow -RedirectStandardOutput $stdout -RedirectStandardError $stderr
            $raw = Get-Content -LiteralPath $stdout -Raw -Encoding utf8
            $report = $raw | ConvertFrom-Json -Depth 40 -ErrorAction Stop
            $err = Get-Content -LiteralPath $stderr -Raw -Encoding utf8
            if ($process.ExitCode -ne $case.Code -or $report.version -ne '2.1.0' -or
                $report.runs.Count -ne 1 -or $report.runs[0].results.Count -ne $case.Results -or
                -not [string]::IsNullOrWhiteSpace($err) -or $raw.Contains([char]27)) {
                throw "SARIF CLI contract mismatch: $($case.Name); exit $($process.ExitCode)"
            }
            if ($case.Code -eq 0) {
                $results = @($report.runs[0].results)
                if (-not ($results | Where-Object { $_.locations.Count -gt 0 })) {
                    throw 'Valid fixture has no source-anchored SARIF findings'
                }
                $uris = @($results | ForEach-Object { $_.locations } | ForEach-Object { $_.physicalLocation.artifactLocation.uri })
                if (-not ($uris | Where-Object { $_ -like 'examples/valid-pbir-tmdl/*' })) {
                    throw 'Source root did not rebase repository paths'
                }
            }
            Write-Host "PASS SARIF $($case.Name): exit $($case.Code)"
        }
    }
    finally { Pop-Location }
}
finally { Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue }
