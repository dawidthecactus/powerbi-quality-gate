# CLI reference

PBIQ exposes three commands: `scan`, `inspect`, and `audit`. Each accepts **one positional path** to an existing `.pbip` file or a directory containing exactly one `.pbip`. The tool works locally and does not write to or refresh a Power BI model.

## How to invoke the CLI

Use the form that matches your installation:

```powershell
# Standalone Windows installation
& 'C:\Tools\PBIQ\pbiq.exe' --help

# From the repository root, after building .NET 10 Release
# (same command syntax follows the DLL path)
dotnet ./src/PowerBIQualityGate.Cli/bin/Release/net10.0/pbiq.dll --help
```

```bash
# Standalone Linux installation
./pbiq/pbiq --help
```

`pbiq --version` prints the executable's actual version. The RC may include build metadata such as `0.2.0-rc.1+<commit>`. The large `scan` banner may display a simplified product version; the `--version` value is authoritative.

## Command overview

| Syntax | Purpose | Model requirement |
| --- | --- | --- |
| `pbiq scan <path> [--no-color]` | Check implemented PBIP structure rules (`PBI-001`–`PBI-018`) | `.pbip` and readable referenced project files |
| `pbiq inspect <path> [--no-color]` | Preview local TMDL declarations/inventory | Local TMDL |
| `pbiq inspect <path> --tom [--no-color]` | Preview semantic-model metadata via Microsoft TOM | Local TMDL |
| `pbiq inspect <path> --measures [--no-color]` | Aggregate measure format/type metadata, excluding measure names and DAX | Local TMDL |
| `pbiq audit <path> [options]` | Evaluate `SM-001` through `SM-004` | Local TMDL |

Run `pbiq <command> --help` for the parser's live usage message.

## `scan`: project structure

```powershell
$cli = './src/PowerBIQualityGate.Cli/bin/Release/net10.0/pbiq.dll'
dotnet $cli scan './examples/valid-pbir-tmdl'
# Exit 0: implemented structural checks passed.

dotnet $cli scan './examples/invalid-pbip-shortcut' --no-color
# Exit 1: structural issue; read the PBI diagnostic.
```

`scan` recognizes local PBIP layout, report and semantic-model references, and supported metadata files. It performs implemented structural checks, **not** full Microsoft JSON Schema validation, DAX evaluation, or model correctness testing. Color/ASCII artwork is for humans, not a machine-readable format.

Exit codes: `0` passed implemented structural checks, `1` structural failure, `2` discovery failure.

## `inspect`: TMDL metadata

```powershell
dotnet $cli inspect './examples/valid-pbir-tmdl'
dotnet $cli inspect './examples/valid-pbir-tmdl' --tom
dotnet $cli inspect './examples/valid-pbir-tmdl' --measures
```

- **Default:** lightweight TMDL declaration/inventory preview.
- **`--tom`:** model details as loaded by Microsoft's TOM serializer. This mode can include object metadata and should not be pasted into public issues without redaction.
- **`--measures`:** aggregate-only inventory: measure totals (including hidden), format kind of *visible* measures (`Static`, `Dynamic`, `Missing`), type kind (`Numeric`, `NonNumeric`, `Unknown`, `Unsupported`), and metadata notes. The input project filename is still displayed. No measure names, DAX expressions, or format-string values appear in this view.

`--tom` and `--measures` are **mutually exclusive** (exit `2`). `inspect` returns `1` if prerequisite structural checks fail and `2` for discovery, incompatible format, deserialization, or usage errors. A successful supported inspection returns `0`.

## `audit`: semantic-model quality gate

```powershell
dotnet $cli audit './examples/valid-pbir-tmdl'
dotnet $cli audit './examples/valid-pbir-tmdl' --details
dotnet $cli audit './examples/valid-pbir-tmdl' --quiet

dotnet $cli audit './examples/warning-pbir-tmdl' --fail-on-warning --no-color
# Exit 1 for this intentionally warning-bearing fixture.

dotnet $cli audit './examples/valid-pbir-tmdl' --min-measure-coverage 95 --quiet
# Exit 1: synthetic fixture has unresolved measure types.
```

### Audit options

| Option | Behavior |
| --- | --- |
| `--details` | Prints grouped findings, affected object locations, and consolidated WHY IT MATTERS / HOW TO FIX guidance. May expose model metadata. |
| `--quiet` | Prints exactly one plain-text verdict line, including finding counts and exit code, suitable for CI and scripts. |
| `--fail-on-warning` | Treats one or more Warning findings as a failing policy (exit `1`); informational findings stay nonblocking. |
| `--min-measure-coverage <0..100>` | Requires an integer percentage of **SM-002 numeric-type classification coverage**; a shortfall fails with exit `1`. Omitting the option imposes no minimum. |
| `--no-color` | Disables terminal color/control styling without changing counts, policies or exit codes. |

`--details` and `--quiet` are **mutually exclusive** (exit `2`). Invalid or out-of-range coverage thresholds also return `2`. The threshold is separate from `--fail-on-warning` and may be combined with it.

### Shipped semantic-model rules

| ID | Checks | Default severity |
| --- | --- | --- |
| `SM-001` | Active relationships with bidirectional cross-filter direction | Warning |
| `SM-002` | Visible **confirmed-numeric** measures missing usable format metadata | Warning |
| `SM-003` | Visible technical/key columns by conservative naming pattern | Information |
| `SM-004` | Inactive relationships | Information |

All four rules operate on metadata and are **review suggestions**, not universal modeling errors. `SM-002` does not assume a numeric type from DAX when offline TOM returns `Unknown` or unsupported. Instead, the audit reports classification coverage and aggregate review items.

## Formatting and privacy

`--no-color` (all three commands) and the `NO_COLOR` environment variable disable ANSI styling. Redirected output also suppresses styling; `audit --quiet` is plain even in an interactive terminal. This is **not** a redaction feature: `scan`, `inspect --tom`, and `audit --details` can include project paths or model names.

For screenshots and public examples, use only the included synthetic fixtures. On a private model, prefer `inspect --measures` or `audit --quiet` for aggregate reporting, and review even these outputs before sharing.

## Return codes and streams

| Code | `scan` | `inspect` | `audit` |
| :--: | --- | --- | --- |
| `0` | Implemented structure checks passed | Inspection completed | Rules evaluated and selected policy allows the findings |
| `1` | Structural validation failed | Prerequisite structural validation failed | Quality/policy failure or prerequisite structural failure |
| `2` | Project discovery failed | Bad input, unsupported model, TOM/read error, or incompatible options | Bad input/flags, unsupported model, execution/integrity error, or no evaluated rules |

Completed, successful audits write to **stdout**; blocking or failed audits write to **stderr**. Never treat exit `2` as a clean or evaluated report. For more examples, see [the output guide](output-guide.md) and [CI integration](ci-integration.md).
