<div align="center">

# 🌵 PBIQ

### Power BI Quality Gate

**Scan PBIP structure. Inspect TMDL metadata. Gate semantic-model quality.**

An offline, read-only CLI for Power BI Project (`.pbip`) files, built for analysts,
BI developers and CI pipelines.

[![CI](https://github.com/dawidthecactus/powerbi-quality-gate/actions/workflows/ci.yml/badge.svg)](https://github.com/dawidthecactus/powerbi-quality-gate/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![Windows and Linux](https://img.shields.io/badge/platform-Windows%20%7C%20Linux-374151)
[![MIT](https://img.shields.io/badge/license-MIT-2ea44f)](LICENSE)
![Release candidate](https://img.shields.io/badge/current%20source-0.2.0--rc.1-f59e0b)

**[Quick start](#quick-start) · [Commands](#three-commands-one-workflow) · [Quality gates](#use-it-as-a-quality-gate) · [Docs](#documentation)**

</div>

> [!IMPORTANT]
> **Release status:** the source currently targets `0.2.0-rc.1`. The Windows/Linux
> packages have passed an Actions dry run, but **v0.2.0 has not been published
> as a GitHub Release**. Build from source to try the latest functionality, or
> use a temporary artifact from a successful [Release workflow](https://github.com/dawidthecactus/powerbi-quality-gate/actions/workflows/release.yml)
> (GitHub sign-in may be required). Do not confuse a dry-run artifact with an
> official release.

## See what PBIQ does

One tool, three levels of feedback:

| | Command | What you learn |
| :-- | :-- | :-- |
| **01** | `scan` | Is the PBIP project structure valid? |
| **02** | `inspect` | What does the local TMDL semantic model contain? |
| **03** | `audit` | Which metadata quality rules need attention, and should CI block? |

A real, synthetic-fixture example of the **single-line CI output**:

```text
$ pbiq audit examples/valid-pbir-tmdl --quiet
PBIQ | AUDIT: PASS WITH COVERAGE GAPS | 0 errors, 0 warnings, 3 information | exit code: 0
```

`PASS WITH COVERAGE GAPS` is intentionally not the same as “everything is
correct.” Some measure types remain `Unknown` in offline TOM, so numeric-format
applicability cannot always be confirmed. See [how to interpret the output](#understand-the-results).

<!-- MEDIA SLOT (02.5.3.3 / 02.5.3.4):
     Insert a real, sanitized, COLOR terminal screenshot or GIF here when it
     exists in assets/. Do not link to a missing file. The source of truth is
     docs/readme-media-plan-02.5.3.1.md.
     Target: assets/demos/pbiq-quick-tour.gif (or a static scan hero PNG).
-->

## Quick start

### Option A: build the current release candidate from source

Requires [Git](https://git-scm.com/) and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
Run these commands from PowerShell 7 or a shell with `dotnet` available:

```powershell
git clone https://github.com/dawidthecactus/powerbi-quality-gate.git
cd powerbi-quality-gate

dotnet restore
dotnet build -c Release

dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- scan examples/valid-pbir-tmdl
dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- inspect examples/valid-pbir-tmdl --measures
dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- audit examples/valid-pbir-tmdl --quiet
```

The bundled examples are **synthetic regression fixtures**, not reports that
are guaranteed to open or refresh in Power BI Desktop.

For detailed installation steps, see **[Getting started](docs/getting-started.md)**.

### Option B: standalone executable (when a release is available)

Download `pbiq-win-x64.zip` or `pbiq-linux-x64.tar.gz` from
[GitHub Releases](https://github.com/dawidthecactus/powerbi-quality-gate/releases)
**after** a release is published. These are self-contained x64 builds and do
not require a .NET runtime on the target machine.

<details>
<summary><strong>Windows x64 (PowerShell)</strong></summary>

```powershell
Expand-Archive .\pbiq-win-x64.zip -DestinationPath .\pbiq -Force
& .\pbiq\pbiq.exe --version
& .\pbiq\pbiq.exe scan 'C:\Reports\MyReport.pbip'
$LASTEXITCODE
```

</details>

<details>
<summary><strong>Linux x64 (bash)</strong></summary>

```bash
mkdir -p pbiq
tar -xzf pbiq-linux-x64.tar.gz -C pbiq
./pbiq/pbiq --version
./pbiq/pbiq scan ./reports/MyReport.pbip
echo $?
```

</details>

macOS and ARM64 standalone distributions are **not yet available**.
See the [release process](docs/release-process.md) for packaging, checksums,
and the candidate-to-stable release policy.

## Three commands, one workflow

All commands take a `.pbip` file or a directory containing **exactly one**
`.pbip` entry point. Replace `<path>` with your own project path.

| Command | Purpose | Useful options |
| --- | --- | --- |
| `pbiq scan <path>` | PBIP discovery, report/model references and implemented structural checks (`PBI-001`–`PBI-018`) | `--no-color` |
| `pbiq inspect <path>` | Lightweight, local TMDL declaration inventory | `--no-color` |
| `pbiq inspect <path> --tom` | Semantic-model metadata through the official Microsoft TOM reader | `--no-color` |
| `pbiq inspect <path> --measures` | Privacy-conscious **aggregate** measure formatting and type inventory | `--no-color` |
| `pbiq audit <path>` | Four semantic-model metadata checks (`SM-001`–`SM-004`) | `--details`, `--quiet`, `--fail-on-warning`, `--min-measure-coverage 95`, `--no-color` |

`inspect --tom` and `inspect --measures` cannot be used together.
`audit --quiet` and `audit --details` are also mutually exclusive.

**Tip:** Add `--no-color` or set `NO_COLOR` for plain terminal logs. Leave it
out when taking the **color** screenshots. The detailed audit includes model
object locations, so do not paste its output into public issues without
reviewing it first. See the **[complete CLI reference](docs/commands.md)**.

## Understand the results

| Audit status | What it means |
| --- | --- |
| **PASS** | Evaluated rules did not report blocking findings under the selected policy. |
| **PASS WITH COVERAGE GAPS** | No blocking finding, but some measure-type eligibility could not be determined from offline metadata. |
| **FAIL (WARNING POLICY)** | `--fail-on-warning` is active and a Warning was found. |
| **FAIL (COVERAGE POLICY)** | Measure-type classification coverage was below your explicit minimum. |

The four current metadata rules:

| Rule | Checks | Default severity |
| --- | --- | --- |
| `SM-001` | Active bidirectional relationships | Warning |
| `SM-002` | Missing format on a **confirmed numeric** visible measure | Warning |
| `SM-003` | Visible technical/key columns | Information |
| `SM-004` | Inactive relationships | Information |

For `SM-002`, the public example can have **2/2 measure formats inspected**
but **0/2 candidate types classified**. These are different measurements. A
format marked `Missing` for an unknown type is a *review item*, not
automatically a warning. PBIQ does not infer numeric types from DAX.

### Exit codes

| Code | `audit` | `scan` |
| :--: | --- | --- |
| `0` | Evaluated, no blocking finding under the chosen policy | Implemented structural checks passed |
| `1` | Blocking finding/policy, or structural validation failure | Structural validation failed |
| `2` | Unable to evaluate, bad input or incompatible options | Project discovery failed |

**Exit `0` is not a DAX, refresh, performance or Power BI Desktop certification.**
For the complete status guide, see **[Reading PBIQ output](docs/output-guide.md)**.

## Use it as a quality gate

The default audit is advisory for Warnings. For stricter CI, opt into either
policy independently or combine them:

```powershell
# From a cloned source repository after dotnet build -c Release
$project = 'examples/warning-pbir-tmdl'

dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- audit $project --quiet
# Advisory: warnings are reported, but do not block by default.

dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- audit $project --fail-on-warning --no-color
# Exit 1 for this synthetic fixture (intentional SM-001 warning).

dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- audit examples/valid-pbir-tmdl --min-measure-coverage 95 --quiet
# Exit 1 because the sample's offline measure types are Unknown.
```

For a GitHub Actions workflow, treat nonzero exit codes according to your
policy. The project itself runs builds, tests and CLI regressions on **Windows
and Ubuntu**. See the **[ready-to-adapt GitHub Actions example](docs/ci-integration.md)**.

## Safety, scope and privacy

- **Read-only and offline:** no Power BI service sign-in, data refresh, model
  writeback or DAX execution.
- **Local TMDL required for `inspect` and `audit`:** standalone PBIR, TMSL,
  external model connections and Fabric exports without `.pbip` are not
  supported by those commands.
- **Metadata checks, not a full validator:** structural checks do not implement
  the entire Microsoft JSON Schema; the four SM rules are conservative,
  inspectable suggestions, not proof of a model defect.
- **Review output before sharing:** `scan` and detailed output can include
  project paths and object names; `inspect --measures` avoids printing measure
  names and DAX but still identifies the input project. Never commit private
  PBIP/TMDL files or screenshots containing customer metadata.
- **Path security:** file/path checks are lexical and not hardened against
  hostile symlinks or junctions.

## Documentation

| Guide | Read when you want to... |
| --- | --- |
| **[Getting started](docs/getting-started.md)** | Install or build PBIQ, verify a first run, and troubleshoot basic errors |
| **[CLI command reference](docs/commands.md)** | Find every command mode, option and exit-code contract |
| **[Output & coverage guide](docs/output-guide.md)** | Interpret PASS, warnings, Unknown types and SM-002 coverage |
| **[CI integration](docs/ci-integration.md)** | Add a pinned PBIQ quality gate to GitHub Actions or local scripts |
| [Project structure checks](docs/current-checks.md) | Understand `PBI-001`–`PBI-018` |
| [TMDL and TOM inspector](docs/tom-inspector-preview.md) | Explore supported inspection modes |
| [Measure inspection](docs/measure-inspection-02.4.5.md) | Understand static/dynamic/missing formats and type coverage |
| [Semantic-model rule pack](docs/first-rule-pack.md) | Read the `SM-001`–`SM-004` definitions |
| [Audit output and explanations](docs/minimal-terminal-experience-02.4.4.md) | Interpret summary, details and quiet modes |
| [Coverage-aware quality gate](docs/coverage-aware-gate-02.4.3.md) | Configure a minimum SM-002 classification rate |
| [Real-world validation](docs/measure-hardening-02.4.5.5.md) | Benchmark local models without publishing their metadata |
| [Release and checksum guide](docs/release-process.md) | Test/download self-contained builds |
| [README media plan](docs/readme-media-plan-02.5.3.1.md) | Capture authentic color screenshots and terminal demos |

See also the [synthetic fixtures](examples/README.md),
[contribution guide](CONTRIBUTING.md) and [MIT license](LICENSE).

## Contributing

PRs, bug reports and feature suggestions are welcome. Run `dotnet test -c
Release` and `dotnet format whitespace --verify-no-changes` before submitting
changes. Please use sanitized or synthetic models in public reproductions.
See [CONTRIBUTING.md](CONTRIBUTING.md) for the full workflow.

---

<p align="center"><sub>Built for practical Power BI quality checks, with transparent limits and reproducible results. 🌵</sub></p>
