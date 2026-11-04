# Power BI Quality Gate

[![CI](https://github.com/dawidthecactus/powerbi-quality-gate/actions/workflows/ci.yml/badge.svg)](https://github.com/dawidthecactus/powerbi-quality-gate/actions/workflows/ci.yml)

**A command-line quality gate for Power BI project files.**

`powerbi-quality-gate` (`pbiq`) inspects the structure of a Power BI Project (`.pbip`) and reports actionable diagnostics with machine-friendly exit codes. It is designed to make Power BI project checks repeatable locally and in CI.

> **Early development (pre-1.0).** The current implementation validates project structure. It is **not yet** a DAX linter, a semantic-model best-practice analyzer, or a complete Power BI validator.

## What works today

- Discover a `.pbip` file from an explicit file path or a directory containing exactly one `.pbip` file.
- Parse the PBIP shortcut and resolve its referenced report directory.
- Check the expected report layout for PBIR or PBIR-Legacy.
- Detect a local semantic model in TMDL or TMSL format, or an external model reference.
- Report structural errors with stable diagnostic codes (`PBI-001` through `PBI-018`) and relevant paths.
- Return non-zero exit codes suitable for automated checks.
- Run locally without connecting to the Power BI service or Microsoft Fabric.

For the precise checks, diagnostic catalog, and current limitations, see [Project structure validation](docs/project-structure-validation.md).

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) to build and run the CLI from source.
- Git to clone the repository.
- A Power BI Project saved in PBIP format to scan.

The CLI has not yet been published as a global .NET tool or standalone executable. **Use `dotnet run` from the repository for now.**

## Quick start

Clone and build:

```powershell
git clone https://github.com/dawidthecactus/powerbi-quality-gate.git
cd powerbi-quality-gate
dotnet restore
dotnet build -c Release
```

Show CLI help:

```powershell
dotnet run --project src/PowerBIQualityGate.Cli -- --help
```

Scan a PBIP file (PowerShell example):

```powershell
dotnet run --project src/PowerBIQualityGate.Cli -- scan "C:\Reports\MyReport.pbip"
$LASTEXITCODE
```

Or scan a directory containing a single `.pbip` file:

```powershell
dotnet run --project src/PowerBIQualityGate.Cli -- scan "C:\Reports\MyProject"
```

On macOS/Linux, use a path such as `./reports/MyReport.pbip` and check the exit code with `echo $?`.

### Example output

For a structurally valid project, the CLI reports the discovered entry point, report format, and semantic-model format:

```text
[OK] PBIP entry point: C:\Reports\MyReport.pbip
[OK] Report format: Pbir
[OK] Semantic model: Tmdl
STRUCTURE VALIDATION: PASS (not full schema validation)
```

For a `.pbip` file containing only `{}`:

```text
[OK] PBIP entry point: C:\Reports\Demo.pbip
[Error] PBI-002: Expected PBIP shortcut version '1.0'. (C:\Reports\Demo.pbip)
STRUCTURE VALIDATION: FAIL
```

These are illustrative examples of the CLI output, not reports from a particular user project.

### Exit codes

| Code | Meaning |
| --- | --- |
| `0` | Project discovery and implemented structure checks passed. |
| `1` | A PBIP entry point was found, but structure validation failed. |
| `2` | The input could not be discovered or resolved. |

A `0` **does not guarantee** that Power BI Desktop can open the project or that its model and DAX are correct.

## Architecture

```text
src/
  PowerBIQualityGate.Cli/      # Commands, arguments, console output
  PowerBIQualityGate.Core/     # Discovery, validation, diagnostics
tests/
  PowerBIQualityGate.Tests/    # xUnit tests
docs/
  project-structure-validation.md
.github/workflows/
  ci.yml                       # Windows + Ubuntu CI
```

The Core library does not depend on CLI presentation. The current scan is read-only and does not modify Power BI files.

## Development and checks

Run the same core checks used by CI:

```powershell
dotnet restore
dotnet build -c Release -warnaserror
dotnet test -c Release
dotnet format whitespace --verify-no-changes
```

The GitHub Actions workflow runs builds, tests, and whitespace verification on both **Windows** and **Ubuntu** for pushes to `main` and pull requests.

## Current limitations

- Requires a `.pbip` shortcut with exactly one referenced report; standalone PBIR or Fabric Git exports without `.pbip` are not supported yet.
- Performs structural checks and JSON parsing, **not** full Microsoft JSON Schema validation.
- Detects TMDL/TMSL layouts but does not parse the semantic model or evaluate DAX.
- Does not validate data, relationships, visual behavior, or report performance.
- Does not authenticate to Fabric or verify that an external semantic model is available.
- Path validation is lexical and is **not** a hardened security boundary for untrusted folders containing symlinks or junctions.
- Current tests use synthetic fixtures; validation against real Power BI exports is a future integration-testing goal.

See [the validation specification](docs/project-structure-validation.md) for details.

## Roadmap

- [x] .NET solution, CLI foundation, and unit tests
- [x] PBIP discovery and project-structure diagnostics
- [x] Cross-platform GitHub Actions CI
- [ ] Real-export integration fixtures and broader PBIP entry points
- [ ] Semantic model analysis using TOM/TMDL
- [ ] Configurable quality rules and DAX-focused checks
- [ ] JSON/SARIF reporting and policy-based CI gates
- [ ] Packaged CLI installation and versioned releases

The roadmap is directional; features are not promises of availability or timing.

## Contributing

Issues and feedback are welcome. For coding conventions, test requirements, and pull-request steps, see [CONTRIBUTING.md](CONTRIBUTING.md).

## License

A license has not been added yet. Please check the repository for an explicit license before reusing or redistributing the code. One will be selected before the first tagged public release.
