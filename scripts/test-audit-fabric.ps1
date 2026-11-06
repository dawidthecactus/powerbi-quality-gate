#Requires -Version 7.0
# Smoke-test native Fabric Git folder audits on both Windows and Linux.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$root = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$dll = Join-Path $root 'src/PowerBIQualityGate.Cli/bin/Release/net10.0/pbiq.dll'
if (-not (Test-Path -LiteralPath $dll -PathType Leaf)) {
    throw "Build PBIQ in Release mode before Fabric smoke tests."
}
$temp = Join-Path ([IO.Path]::GetTempPath()) ('pbiq-fabric-smoke-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $temp -Force | Out-Null
Push-Location -LiteralPath $root
try {
    $cases = @(
        @{ Name='fabric workspace'; Args=@('audit','examples/fabric-git','--format','json'); Exit=0; Kind='json' }
        @{ Name='fabric item'; Args=@('audit','examples/fabric-git/Revenue.SemanticModel','--format','sarif','--sarif-source-root','.'); Exit=0; Kind='sarif' }
        @{ Name='fabric warning'; Args=@('audit','examples/fabric-git-warning','--fail-on-warning','--format','sarif','--sarif-source-root','.'); Exit=1; Kind='sarif' }
    )
    foreach ($case in $cases) {
        $stdout = Join-Path $temp 'report.json'
        $stderr = Join-Path $temp 'stderr.txt'
        $process = Start-Process -FilePath dotnet -ArgumentList (@($dll) + $case.Args) `
            -Wait -PassThru -NoNewWindow -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        if ($process.ExitCode -ne $case.Exit) {
            throw "Unexpected exit code for $($case.Name): $($process.ExitCode)"
        }
        $report = Get-Content -LiteralPath $stdout -Raw -Encoding utf8 | ConvertFrom-Json -Depth 50
        if ((Get-Item -LiteralPath $stderr).Length -gt 0) {
            throw "Unexpected stderr for $($case.Name)"
        }
        if ($case.Kind -eq 'json') {
            if ($report.schemaVersion -ne '1.0.0' -or $report.project.name -ne 'Revenue.SemanticModel' -or
                $report.result.exitCode -ne 0 -or $report.summary.rulesEvaluated -ne 4) {
                throw 'Fabric JSON audit contract mismatch.'
            }
        } else {
            if ($report.version -ne '2.1.0' -or $report.runs[0].properties.pbiqExitCode -ne $case.Exit) {
                throw 'Fabric SARIF audit contract mismatch.'
            }
            $located = @($report.runs[0].results | Where-Object { @($_.locations).Count -gt 0 })
            if ($located.Count -eq 0) { throw 'Fabric audit lost source file mapping.' }
            $uri = $located[0].locations[0].physicalLocation.artifactLocation.uri
            if (-not $uri.StartsWith('examples/fabric-git')) { throw "Unexpected Fabric SARIF URI: $uri" }
        }
        Write-Host "PASS $($case.Name): exit $($case.Exit)"
    }
}
finally {
    Pop-Location
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
