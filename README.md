# Power BI Quality Gate

[![CI](https://github.com/dawidthecactus/powerbi-quality-gate/actions/workflows/ci.yml/badge.svg)](https://github.com/dawidthecactus/powerbi-quality-gate/actions/workflows/ci.yml)

**A command-line quality gate for Power BI project files.**

`powerbi-quality-gate` (`pbiq`) performs read-only structural checks on Power BI
Project (`.pbip`) files. It provides a branded terminal report, stable diagnostic
codes, explanations, suggested fixes, and CI-friendly exit codes.

> **Early development (v0.1.x).** A successful scan means the implemented
> **structure checks** passed. This is **not** a DAX linter, model-quality
> analyzer, full PBIR/TMDL validator, or proof a project will open in Desktop.

## What works today

- Find a `.pbip` entry point from a file path or a directory containing exactly one `.pbip`.
- Parse the PBIP shortcut and resolve its referenced report directory.
- Validate the expected PBIR or PBIR-Legacy report structure and JSON files.
- Detect a local TMDL/TMSL semantic model or an external model reference.
- Display an easy-to-read project profile, checkpoints, findings, **WHY IT MATTERS** and **HOW TO FIX** guidance.
- Provide diagnostic codes `PBI-001` through `PBI-018`, file paths, and stable exit codes.
- Run fully offline without modifying project files or contacting Power BI/Fabric.

See [current checks](docs/current-checks.md) and
[project structure validation](docs/project-structure-validation.md) for exact
rules and limitations.

## Install a standalone CLI

**Once the first GitHub Release has been published**, download the matching
archive from [Releases](https://github.com/dawidthecactus/powerbi-quality-gate/releases).
These 64-bit archives include the required .NET runtime, so no .NET SDK or
runtime installation is needed to run `pbiq`.

**Windows x64 (PowerShell):**

```powershell
Expand-Archive .\pbiq-win-x64.zip -DestinationPath .\pbiq -Force
& .\pbiq\pbiq.exe --version
& .\pbiq\pbiq.exe scan 'C:\Reports\MyReport.pbip'
$LASTEXITCODE
```

**Linux x64:**

```bash
mkdir -p pbiq
tar -xzf pbiq-linux-x64.tar.gz -C pbiq
./pbiq/pbiq --version
./pbiq/pbiq scan ./reports/MyReport.pbip
echo $?
```

For now, if the release has not yet been published, build and run from source
using the next section. macOS and ARM64 packages are not available yet.
Release archives come with `SHA256SUMS.txt` for download verification.

## Build and run from source

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
and Git.

```powershell
git clone https://github.com/dawidthecactus/powerbi-quality-gate.git
cd powerbi-quality-gate
dotnet restore
dotnet build -c Release

dotnet run --project src/PowerBIQualityGate.Cli -- --help
dotnet run --project src/PowerBIQualityGate.Cli -- scan examples/valid-pbir-tmdl
```

`scan` accepts either a `.pbip` file or a directory with exactly one such file:

```powershell
dotnet run --project src/PowerBIQualityGate.Cli -- scan 'C:\Reports\MyProject'
```

Disable ANSI colors explicitly, or set the `NO_COLOR` environment variable:

```powershell
dotnet run --project src/PowerBIQualityGate.Cli -- scan examples/valid-pbir-tmdl --no-color
```

The terminal UI includes a PBIQ header, **PROJECT PROFILE**, **SCAN
CHECKPOINTS**, a diagnostic report, and a final PASS/FAIL status. Findings
include explanations and suggested fixes. A successful scan writes to stdout;
failures write to stderr. Redirected output is plain text.
See [console output](docs/console-output.md).

## Included examples

The repository contains **synthetic structural fixtures**, not full exports
from Power BI Desktop:

```powershell
dotnet run --project src/PowerBIQualityGate.Cli -- scan examples/valid-pbir-tmdl
$LASTEXITCODE  # expected: 0

dotnet run --project src/PowerBIQualityGate.Cli -- scan examples/invalid-pbip-shortcut
$LASTEXITCODE  # expected: 1 with PBI-002
```

See [example projects](examples/README.md) for details. A simplified excerpt
from the rendered output looks like this:

```text
PBIQ / POWER BI QUALITY GATE v0.1.0
01 PROJECT PROFILE     Report: PBIR    Model: TMDL
02 SCAN CHECKPOINTS    PBIP found / report found / model found
03 DIAGNOSTIC REPORT   No structural issues detected
04 FINAL RESULT        STRUCTURE VALIDATION: PASS
```

### Exit codes

| Code | Meaning |
| --- | --- |
| `0` | PBIP found and implemented structure checks passed. |
| `1` | PBIP found but one or more structure checks failed. |
| `2` | Project input could not be discovered or resolved. |

Exit code `0` does **not** guarantee that Power BI Desktop can open the project,
or that semantic model content or DAX is correct.

## Architecture

```text
src/
  PowerBIQualityGate.Cli/      # CLI, console report renderer and guidance
  PowerBIQualityGate.Core/     # PBIP discovery and structural validation
tests/
  PowerBIQualityGate.Tests/    # xUnit unit and sample-project tests
examples/                      # Synthetic valid and invalid PBIP fixtures
docs/                          # Checks, console output and release process
.github/workflows/
  ci.yml                       # Windows + Ubuntu CI
  release.yml                  # Dry-run packages and tag-triggered prerelease
```

## Development

```powershell
dotnet restore
dotnet build -c Release -warnaserror
dotnet test -c Release
dotnet format whitespace --verify-no-changes
```

GitHub Actions runs these checks on Windows and Ubuntu. Release preparation
supports a manual packaging dry run, followed by **explicit tagging** to
publish binaries. See [release process](docs/release-process.md).

## Limitations

- A `.pbip` shortcut with exactly one referenced report is currently required;
  standalone PBIR and Fabric Git exports without `.pbip` are not supported.
- Checks are structural and validate JSON parsing, **not** complete Microsoft
  JSON Schema, TMDL parsing, DAX expressions, relationships or performance.
- External models are not contacted or authenticated.
- Path checks are lexical, not hardened against malicious filesystem
  symlinks or junctions.
- Synthetic test projects are not guaranteed to open in Power BI Desktop.

## Roadmap

- [x] .NET CLI foundation and unit tests
- [x] PBIP discovery and structural diagnostics
- [x] Cross-platform GitHub Actions CI
- [x] Synthetic structural fixtures
- [x] Branded terminal UI and diagnostic guidance
- [ ] Tagged release with standalone Windows and Linux binaries
- [ ] Real-export integration fixtures and wider PBIP entry points
- [ ] Semantic model analysis with TOM/TMDL
- [ ] Configurable quality rules and DAX checks
- [ ] JSON/SARIF reporting and policy-based CI gates

Roadmap items describe direction, not guaranteed dates or functionality.

## Contributing

Issues and pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT License](LICENSE).
