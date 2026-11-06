#Requires -Version 7.0
<#
.SYNOPSIS
  Tests an extracted PBIQ distribution on the host operating system.
.DESCRIPTION
  Uses only public synthetic fixtures. Invokes the standalone executable, not
  `dotnet <dll>`, and asserts success, advisory, blocking and invalid CLI paths.
  Captured process output is held in a temporary folder and deleted afterwards.
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
    $file = Join-Path $distribution $name
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) {
        throw "Distribution is missing required file: $name"
    }
}

$baseline = Join-Path $root 'examples/valid-pbir-tmdl'
$warning = Join-Path $root 'examples/warning-pbir-tmdl'
if (-not (Test-Path -LiteralPath $baseline) -or -not (Test-Path -LiteralPath $warning)) {
    throw 'Public synthetic PBIP examples were not found.'
}

$temp = Join-Path ([System.IO.Path]::GetTempPath()) ('pbiq-release-smoke-' + [guid]::NewGuid().ToString('N'))
New-Item -Path $temp -ItemType Directory -Force | Out-Null

try {
    $cases = @(
        @{ Name = 'version'; Arguments = @('--version'); Expected = 0; Marker = $ExpectedVersion; Stream = 'stdout' }
        @{ Name = 'scan'; Arguments = @('scan', $baseline, '--no-color'); Expected = 0; Marker = "POWER BI QUALITY GATE  v$ExpectedVersion"; Stream = 'stdout' }
        @{ Name = 'measure inspection'; Arguments = @('inspect', $baseline, '--measures', '--no-color'); Expected = 0; Marker = 'Measures inspected: 2/2'; Stream = 'stdout' }
        @{ Name = 'audit baseline'; Arguments = @('audit', $baseline, '--quiet'); Expected = 0; Marker = 'PASS WITH COVERAGE GAPS'; Stream = 'stdout' }
        @{ Name = 'audit warning policy'; Arguments = @('audit', $warning, '--fail-on-warning', '--quiet'); Expected = 1; Marker = 'FAIL (WARNING POLICY)'; Stream = 'stderr' }
        @{ Name = 'audit coverage policy'; Arguments = @('audit', $baseline, '--min-measure-coverage', '95', '--quiet'); Expected = 1; Marker = 'FAIL (COVERAGE POLICY)'; Stream = 'stderr' }
        @{ Name = 'invalid output modes'; Arguments = @('audit', $baseline, '--details', '--quiet', '--no-color'); Expected = 2; Marker = 'INVALID OUTPUT MODE'; Stream = 'stderr' }
    )

    foreach ($case in $cases) {
        $stdout = Join-Path $temp 'stdout.txt'
        $stderr = Join-Path $temp 'stderr.txt'
        # The process is executed directly so this checks the actual binary.
        # Start-Process handles intentional exit 1 and 2 identically on Windows/Linux.
        # Start-Process joins ArgumentList into a command line on both platforms.
        # Quote fixture paths explicitly so checkouts under directories with
        # spaces (or user profiles containing spaces) are handled correctly.
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
        $result = Get-Content -LiteralPath $streamPath -Raw
        if ($process.ExitCode -ne $case.Expected -or -not $result.Contains($case.Marker)) {
            Write-Host "FAIL $($case.Name): exit $($process.ExitCode), expected $($case.Expected); marker found: $($result.Contains($case.Marker))"
            throw "Distribution smoke test failed: $($case.Name)"
        }
        if ($result.Contains([char]27)) {
            throw "Distribution smoke test emitted ANSI to captured output: $($case.Name)"
        }
        Write-Host "PASS $($case.Name): exit $($process.ExitCode)"
    }

    Write-Host "PASS distribution $ExpectedVersion ($executableName): all $($cases.Count) smoke cases"
}
finally {
    Remove-Item -LiteralPath $temp -Recurse -Force -ErrorAction SilentlyContinue
}
