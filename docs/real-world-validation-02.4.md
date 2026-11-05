# Milestone 02.4.1: Real-world PBIP compatibility probe

## Why

The synthetic fixtures in PBIQ 02.3 establish correctness of the current rule engine.
Aviation Intelligence, Nielsen and older Power BI projects test **compatibility** with
actual Desktop-generated PBIP structures and TOM features. This probe is diagnostic:
a model warning is not necessarily a defect, and a parser failure is a compatibility
finding rather than a reason to modify production files.

## Requirements

- Windows PowerShell **7+** (`$PSVersionTable.PSVersion`).
- .NET 10 SDK and the PBIQ source repository, already built with `dotnet build -c Release`.
- Local copies of the actual projects. The script **cannot access paths on a different computer**.
- PBIP + local TMDL for `pbiq inspect --tom` and `pbiq audit`.

## How to run

Use `scripts/real-world-validation.ps1` from the PBIQ repository root with
`-ProjectRoots`, optional `-GroupNames`, and optional `-PathNamePatterns`
(one regular expression per root, matched against discovered file paths).
The script searches at most six directory levels below each root by default.
Increase `-MaxDepth` if PBIPs are nested more deeply. Common caches, `.git`,
`node_modules`, `bin`, `obj`, and `data` folders are skipped.

To only discover files without running commands, use `-DiscoveryOnly`.
To avoid writing DAX-expression previews to private logs, use `-SkipInspect`.

## What it runs

For each discovered `.pbip`, if structural scan succeeds:

1. `pbiq scan <project> --no-color`
2. `pbiq inspect --tom <project> --no-color` (optional)
3. `pbiq audit <project> --no-color`
4. `pbiq audit <project> --fail-on-warning --no-color`

The CLI is invoked from the already-built release DLL (`dotnet pbiq.dll`).
No build logs pollute the audit output. Nonzero audit exit codes are captured
as results, not treated as batch failure.

Files in `.pbix`, `.pbit` or `.zip` format are **reported but not opened or
converted**. This is deliberate. For PBIX/PBIT, open a copy in Power BI Desktop
and use **File > Save as > Power BI Project (.pbip)**. Make sure the model is
saved as **TMDL** (`*.SemanticModel/definition/*.tmdl`). Never overwrite the
original project just to run the probe.

## Output and privacy

Default output directory:
`%USERPROFILE%/Documents/PBIQ-RealWorld-Audits/run-<timestamp>-<id>/`

- `share-summary.csv`: aliases and exit codes, counts and states; **no paths or
  DAX**. Review this file before sharing even though it is designed to be safe.
- `private-results.csv`: includes full local paths. Do not publish.
- `<group>-01/scan.txt`, `inspect-tom.txt`, `audit-default.txt`,
  `audit-strict.txt`: full diagnostic logs **may contain proprietary model names,
  formulas, object names, paths or exceptions**. Do not publish.
- `README-PRIVATE.txt`: instructions about confidentiality.

The script never copies the PBIP sources, refreshes models, connects to Fabric,
rewrites TMDL, edits Git configuration, or uploads data anywhere. Output is
outside the repository to reduce the risk of accidentally committing it.

## Interpreting results

| Scan | TOM | Default audit | Strict audit | Interpretation |
| --- | --- | --- | --- | --- |
| 0 | 0 | 0 | 0 | PBIP/TMDL supported, no blocking warnings |
| 0 | 0 | 0 | 1 | PBIP/TMDL supported; warning-based gate correctly blocks |
| 1 | - | - | - | Structural compatibility failure, inspect `scan.txt` |
| 0 | 2 | - | - | TOM parser cannot load the real model, inspect `inspect-tom.txt` |
| - | - | - | - | PBIX/PBIT/ZIP not directly supported, or discovery only |

`scan` and TOM use read-only metadata analysis. An exit `0` **does not** prove
that DAX evaluates correctly, refresh succeeds, or the report works in Desktop.

## Suggested acceptance criteria for Milestone 02.4

- Run Aviation PBIP with `scan`, TOM and both audit policies.
- Inventory all Nielsen versions and identify which need PBIX → PBIP conversion.
- Identify the exact artifact type for the older report and record whether it
  can be audited today.
- Analyze failures by class: discovery, structural validation, TOM deserialization,
  or actual rule finding; fix PBIQ only if a reproducible tool defect is found.
- Add non-sensitive, synthetic regression fixtures for newly discovered bugs.
- Verify Windows and Ubuntu CI after **code** changes; local absolute paths
  must never be introduced into CI tests.

Reference: https://learn.microsoft.com/en-us/power-bi/developer/projects/projects-overview
