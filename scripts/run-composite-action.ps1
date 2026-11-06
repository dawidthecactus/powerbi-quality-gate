#Requires -Version 7.0
# Executed by the composite action, not a replacement for the PBIQ CLI.
# Native nonzero exits are captured so a policy failure still leaves SARIF.
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$PSNativeCommandUseErrorActionPreference = $false

function Resolve-WorkspaceRelativePath {
    param(
        [Parameter(Mandatory)][string] $Workspace,
        [Parameter(Mandatory)][string] $Relative,
        [Parameter(Mandatory)][string] $Label
    )

    if ([string]::IsNullOrWhiteSpace($Relative) -or $Relative.Contains("`n") -or $Relative.Contains("`r") -or
        [IO.Path]::IsPathRooted($Relative)) {
        throw "$Label must be a nonempty, workspace-relative path without newlines."
    }

    $absolute = [IO.Path]::GetFullPath([IO.Path]::Combine($Workspace, $Relative))
    $check = [IO.Path]::GetRelativePath($Workspace, $absolute)
    if ($check -eq '..' -or $check.StartsWith('..' + [IO.Path]::DirectorySeparatorChar) -or
        $check.StartsWith('..' + [IO.Path]::AltDirectorySeparatorChar) -or
        [IO.Path]::IsPathRooted($check)) {
        throw "$Label must remain inside GITHUB_WORKSPACE."
    }
    return $absolute
}

$exitCode = 2
$sarifPath = ''
$evaluated = 'false'
try {
    if ([string]::IsNullOrWhiteSpace($env:GITHUB_WORKSPACE) -or
        [string]::IsNullOrWhiteSpace($env:PBIQ_ACTION_PATH)) {
        throw 'GITHUB_WORKSPACE and PBIQ_ACTION_PATH are required.'
    }
    $workspace = [IO.Path]::GetFullPath($env:GITHUB_WORKSPACE)
    if (-not (Test-Path -LiteralPath $workspace -PathType Container)) {
        throw 'GITHUB_WORKSPACE must exist.'
    }

    $project = Resolve-WorkspaceRelativePath -Workspace $workspace -Relative $env:PBIQ_PROJECT_PATH -Label 'project-path'
    if (-not (Test-Path -LiteralPath $project)) {
        throw 'project-path does not exist in the checked-out repository.'
    }
    $destination = Resolve-WorkspaceRelativePath -Workspace $workspace -Relative $env:PBIQ_SARIF_OUTPUT -Label 'sarif-output'
    if ([IO.Path]::GetExtension($destination) -ne '.sarif') {
        throw 'sarif-output must end in .sarif.'
    }

    $failOnWarning = $env:PBIQ_FAIL_ON_WARNING
    if ($failOnWarning -cnotin @('true', 'false')) {
        throw 'fail-on-warning must be true or false.'
    }
    $threshold = $env:PBIQ_MIN_MEASURE_COVERAGE
    if ($threshold -ne '' -and $threshold -cnotmatch '^(?:0|[1-9][0-9]?|100)$') {
        throw 'min-measure-coverage must be an integer from 0 through 100.'
    }

    $cliProject = Join-Path $env:PBIQ_ACTION_PATH 'src/PowerBIQualityGate.Cli/PowerBIQualityGate.Cli.csproj'
    $cliDll = Join-Path $env:PBIQ_ACTION_PATH 'src/PowerBIQualityGate.Cli/bin/Release/net10.0/pbiq.dll'
    if (-not (Test-Path -LiteralPath $cliProject -PathType Leaf)) {
        throw 'The pinned action revision does not contain the expected PBIQ CLI source.'
    }

    & dotnet restore $cliProject
    if ($LASTEXITCODE -ne 0) { throw 'PBIQ dependency restore failed.' }
    & dotnet build $cliProject -c Release --no-restore -warnaserror
    if ($LASTEXITCODE -ne 0) { throw 'PBIQ CLI build failed.' }

    $arguments = @($cliDll, 'audit', $project, '--format', 'sarif', '--sarif-source-root', $workspace)
    if ($failOnWarning -ceq 'true') { $arguments += '--fail-on-warning' }
    if ($threshold -ne '') { $arguments += @('--min-measure-coverage', $threshold) }
    if ($env:PBIQ_CONFIG_PATH -ne '') {
        $config = Resolve-WorkspaceRelativePath -Workspace $workspace -Relative $env:PBIQ_CONFIG_PATH -Label 'config-path'
        if (-not (Test-Path -LiteralPath $config -PathType Leaf)) {
            throw 'config-path does not exist in the checked-out repository.'
        }
        $arguments += @('--config', $config)
    }

    $parent = Split-Path -Parent $destination
    if (-not (Test-Path -LiteralPath $parent -PathType Container)) {
        New-Item -ItemType Directory -Path $parent -Force | Out-Null
    }

    # Redirect only CLI stdout: never mix build logs into the SARIF JSON.
    & dotnet @arguments 1> $destination
    $exitCode = $LASTEXITCODE
    if ($exitCode -notin @(0, 1, 2)) {
        throw 'PBIQ returned an unexpected process exit code.'
    }

    $sarif = Get-Content -LiteralPath $destination -Raw -Encoding utf8 | ConvertFrom-Json -Depth 50
    if ($sarif.version -ne '2.1.0' -or @($sarif.runs).Count -ne 1 -or
        $sarif.runs[0].tool.driver.name -ne 'pbiq' -or
        $sarif.runs[0].properties.pbiqExitCode -ne $exitCode -or
        @($sarif.runs[0].invocations).Count -ne 1 -or
        $sarif.runs[0].invocations[0].executionSuccessful -isnot [bool]) {
        throw 'PBIQ did not produce a valid SARIF 2.1.0 audit envelope.'
    }
    $completed = [bool]$sarif.runs[0].invocations[0].executionSuccessful
    if (($completed -and $exitCode -eq 2) -or (-not $completed -and $exitCode -eq 0)) {
        throw 'PBIQ exit code and SARIF evaluation status disagree.'
    }
    $evaluated = if ($completed) { 'true' } else { 'false' }
    $sarifPath = $destination
    Write-Host "PBIQ audit exit code: $exitCode. Evaluated: $evaluated. SARIF report generated."
}
catch {
    # Never silently treat infrastructure/argument failures as a clean audit.
    $exitCode = 2
    $sarifPath = ''
    $evaluated = 'false'
    Write-Host ('::error title=PBIQ composite action::' + $_.Exception.Message.Replace("`r", ' ').Replace("`n", ' '))
}
finally {
    if (-not [string]::IsNullOrWhiteSpace($env:GITHUB_OUTPUT)) {
        Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8 -Value "exit-code=$exitCode"
        Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8 -Value "sarif-path=$sarifPath"
        Add-Content -LiteralPath $env:GITHUB_OUTPUT -Encoding utf8 -Value "evaluated=$evaluated"
    }
}

# Explicit failure occurs in the next composite step, after outputs are saved.
exit 0
