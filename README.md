# Power BI Quality Gate

[![CI](https://github.com/dawidthecactus/powerbi-quality-gate/actions/workflows/ci.yml/badge.svg)](https://github.com/dawidthecactus/powerbi-quality-gate/actions/workflows/ci.yml)

**A read-only command-line quality gate for Power BI Project (`.pbip`) files.**

`pbiq` validates PBIP project structure, inspects local TMDL metadata with
Microsoft TOM, and runs four transparent semantic-model quality rules with
**WHY IT MATTERS** and **HOW TO FIX** guidance. It is designed for offline
analysis and automated build checks.

> **Early development (v0.1.x).** `scan` covers structural checks, while `audit`
> runs a limited **metadata-only** rule pack. Neither command validates DAX,
> refreshes data, or guarantees that a Power BI Desktop model works correctly.

## What works today

- Find a `.pbip` entry point in a file path or a directory containing one PBIP.
- Parse a PBIP shortcut, validate local report/project structure and JSON files.
- Detect local TMDL/TMSL or external semantic-model references.
- Display the shared branded PBIQ terminal report with stable `PBI-001`–`PBI-018` diagnostics.
- Inspect a local TMDL model using the Microsoft TOM serializer (`inspect --tom`).
- Inventory formatting and TOM data types of every measure with privacy-safe `inspect --measures` (without DAX evaluation).
- Audit TOM-derived metadata with `SM-001`–`SM-004`:
  bidirectional relationships, visible numeric-measure formatting, visible
  technical columns and inactive relationships.
- Enable `audit --fail-on-warning` to block on warnings in CI.
- Show `PASS WITH COVERAGE GAPS` for unclassified measure types and optionally
  enforce `audit --min-measure-coverage 95` in CI.
- Inspect per-rule **OBJECT COVERAGE**, including evaluated/omitted model objects
  and why `SM-002` skipped unknown TOM measure types.
- Run read-only and offline, with structured exit codes and `--no-color` output.
- Test on Windows and Ubuntu through GitHub Actions.

See [audit coverage](docs/audit-coverage-02.4.2.md),
[quality gate policies](docs/quality-gate-ci.md),
[rule definitions](docs/first-rule-pack.md),
[current structural checks](docs/current-checks.md), and
[TOM inspector details](docs/tom-inspector-preview.md).

## Install a standalone CLI

**Once the next GitHub Release is published**, download the matching archive
from [Releases](https://github.com/dawidthecactus/powerbi-quality-gate/releases).
The 64-bit self-contained archives require no locally installed .NET runtime.
The existing v0.1.x release artifacts may predate the latest `audit` command:
**build from source for current development functionality**.

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

macOS and ARM64 standalone packages are not available yet. See the
[release process](docs/release-process.md).

## Build and run from source

Requirements: [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
and Git.

```powershell
git clone https://github.com/dawidthecactus/powerbi-quality-gate.git
cd powerbi-quality-gate
dotnet restore
dotnet build -c Release

dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- --help
dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- scan examples/valid-pbir-tmdl
dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- inspect examples/valid-pbir-tmdl --tom
dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- audit examples/valid-pbir-tmdl
```

### Warning-based quality gates

A separate synthetic example contains a bidirectional relationship that
triggers `SM-001` Warning:

```powershell
dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- audit examples/warning-pbir-tmdl
$LASTEXITCODE # 0: advisory by default

dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- audit examples/warning-pbir-tmdl --fail-on-warning --no-color
$LASTEXITCODE # 1: strict warning policy blocks
```

The compact default lists finding counts and coverage. Add `--details` for
rule-grouped model-object locations, **WHY IT MATTERS** and **HOW TO FIX**;
add `--quiet` for a single status line. Colors can be disabled with
`--no-color` or `NO_COLOR`.
Redirected output is plain text. See [console output](docs/console-output.md)
and [audit terminal experience](docs/audit-terminal-experience.md).

### Coverage-aware audits

`pbiq audit` reports when the offline TOM metadata did not classify a visible
measure's type. The status `PASS WITH COVERAGE GAPS` is **not** a claim that
its formatting is correct. Require a minimum classification rate for SM-002:

```powershell
dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- audit examples/valid-pbir-tmdl --min-measure-coverage 95
$LASTEXITCODE # 1: sample measure types are Unknown, so SM-002 coverage is below 95%
```

No minimum is enforced by default. The threshold is a whole percent from 0 to
100, and it can be used together with `--fail-on-warning`. See
[coverage-aware gate](docs/coverage-aware-gate-02.4.3.md).

## Measure metadata inventory (Milestones 02.4.5.1 + 02.4.5.2)

The new **read-only** inventory classifies each measure on two independent axes:
format metadata (`Static`, `Dynamic`, `Missing`) and offline TOM data type
(`Numeric`, `NonNumeric`, `Unknown`, `Unsupported`). Hidden measures are counted
in the total but excluded from the visible-format/type breakdown. A present
format does **not** prove that a measure is numeric or correctly formatted.

```powershell
dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- inspect examples/valid-pbir-tmdl --measures --no-color
```

This view prints only aggregate counts, without individual table/measure names,
DAX expressions, or format-string values. `--measures` and `--tom` are mutually
exclusive. See [measure inspection](docs/measure-inspection-02.4.5.md).

**Important:** The existing SM-002 rule, `audit` verdicts and
`--min-measure-coverage` policy are intentionally unchanged in this stage.
The newer metadata-inspection percentage must not be mistaken for numeric
classification coverage.

## Minimal audit output (Milestone 02.4.4)

The `audit` command now defaults to a compact overview: project totals, gate
verdict, finding counts grouped by rule, SM-002 measure-type coverage and active
policy. It does not enumerate confidential object locations or repeat remediation
text. To access every finding, use the opt-in detailed view. To get one plain
line for shell scripts, use quiet mode:

```powershell
dotnet run --project src/PowerBIQualityGate.Cli -- audit examples/valid-pbir-tmdl
dotnet run --project src/PowerBIQualityGate.Cli -- audit examples/valid-pbir-tmdl --details
dotnet run --project src/PowerBIQualityGate.Cli -- audit examples/valid-pbir-tmdl --quiet
```

`--details` groups findings by rule and prints affected locations plus WHY/HOW
once per identical guidance group. `--quiet` always emits a single plain-text
line, without ANSI codes or project metadata. `--details` and `--quiet` are
mutually exclusive (exit `2`). `--no-color`, `NO_COLOR`, `--fail-on-warning`
and `--min-measure-coverage` continue to work; gate exit codes and stdout/stderr
routing are unchanged. See [minimal terminal experience](docs/minimal-terminal-experience-02.4.4.md).

### Exit codes for `pbiq audit`

| Code | Meaning |
| --- | --- |
| `0` | At least one rule evaluated and no blocking findings under the selected policy. |
| `1` | Structural validation failed, Error finding reported, Warning finding blocked by `--fail-on-warning`, or SM-002 coverage below an explicitly configured minimum. |
| `2` | Input/discovery/format/TOM/rule execution problem or no evaluated rules; audit not successfully evaluated. |

`pbiq scan` retains its original structural exit-code contract. An audit
with exit `0` is **not** a full-model or DAX certification.

## Included examples

- [`valid-pbir-tmdl`](examples/valid-pbir-tmdl): minimal TOM/structural baseline,
  informational technical-key findings.
- [`warning-pbir-tmdl`](examples/warning-pbir-tmdl): intentional SM-001 warning;
  demonstrates `--fail-on-warning`.
- [`invalid-pbip-shortcut`](examples/invalid-pbip-shortcut): broken PBIP shortcut,
  structural validation failure.

See [example descriptions](examples/README.md). The examples are synthetic,
not full Power BI Desktop exports or refreshable models.

## Architecture

```text
src/
  PowerBIQualityGate.Cli/      # CLI commands and terminal renderers
  PowerBIQualityGate.Core/     # Discovery, structure, TOM snapshots, quality rules and policy
tests/
  PowerBIQualityGate.Tests/    # xUnit tests using synthetic projects
examples/                      # Baseline, warning and invalid PBIP fixtures
docs/                          # Checks, rule catalog, CI usage and release guidance
.github/workflows/
  ci.yml                       # Build, tests, CLI exit-code smoke tests on Windows + Ubuntu
  release.yml                  # Standalone packaging / release automation
```

## Development

```powershell
dotnet restore
dotnet build -c Release -warnaserror
dotnet test -c Release
dotnet format whitespace --verify-no-changes
git diff --check
```

GitHub Actions runs the checks on Windows and Ubuntu and verifies the exit
codes from **real CLI processes**. See [CI policy guide](docs/quality-gate-ci.md).

## Limitations

- A `.pbip` shortcut is currently required. Standalone PBIR and Fabric Git
  exports without `.pbip` are not supported.
- Structural checking does not implement complete Microsoft JSON Schema.
- TOM deserialization does not evaluate DAX or query results or refresh data.
- The four quality rules are conservative review suggestions, not proof of a
  model defect. Object coverage reports the actual metadata scope; numeric
  formatting is skipped when the offline TOM type is unknown or unsupported.
- External semantic models are not contacted or authenticated.
- Path checks are lexical and not hardened against malicious symlinks or junctions.

## Roadmap

- [x] .NET CLI foundation and unit tests
- [x] PBIP discovery and structural diagnostics
- [x] Cross-platform GitHub Actions CI
- [x] Branded terminal UI and guidance
- [x] Basic TOM/TMDL semantic-model inspection
- [x] First metadata rule pack and optional warning-based CI gate
- [ ] Real-world Power BI Desktop export regression fixtures
- [x] Explicitly report SM-002 coverage gaps and optional CI threshold
- [ ] Per-rule configuration and suppression
- [ ] Deeper DAX checks, JSON/SARIF reporting and policy profiles
- [ ] Publish an updated standalone release with the current audit features

Roadmap items describe direction, not guaranteed delivery dates.

## Contributing

Issues and pull requests are welcome. See [CONTRIBUTING.md](CONTRIBUTING.md).

## License

[MIT License](LICENSE).
