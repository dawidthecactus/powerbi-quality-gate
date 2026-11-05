# Milestone 02.4.5.5: Real-World Validation & Hardening

## Why

A synthetically green test suite is not enough for a metadata-only Power BI
quality tool. This phase adds a safety check against internally contradictory
SM-002 totals and a **local, read-only, privacy-safe** validation/benchmark
runner. It retains the 02.4.5.4 user interface and all existing gate policies.

## Internal invariant guard

`MeasureQualityConsistencyValidator` checks, after SM-002 is evaluated:

- All three measure totals agree with the TOM snapshot, including hidden measures.
- Visible and hidden partitions add up, and hidden counts agree across reports.
- Static, dynamic and missing formats partition visible measures exactly once.
- Numeric, nonnumeric, unknown and unsupported types partition visible measures.
- Blank/unavailable dynamic-expression counts and dual-format notes do not exceed their parent populations.
- Legacy `--min-measure-coverage` type-classification counters agree with the new inventory.
- The assessment categories account for every measure exactly once.
- Confirmed SM-002 numeric-missing warnings match the actual SM-002 Warning findings, not warnings from unrelated rules.

A mismatch produces `MEASURE QUALITY INTEGRITY FAILED` with **exit code 2**,
without claiming a PASS. The diagnostic uses only stable error codes (not names
or DAX). A valid project still uses the original `0/1/2` exit-code contract.
The validator does not attempt to infer DAX return types or evaluate expressions.

## Local project validation

Build in Release, then use **PowerShell 7**:

```powershell
dotnet build -c Release -warnaserror

# Point these variables at private, local, TMDL-backed PBIP files:
$paths = @($aviation, $nc, $nielsen, $nielsenCopy)
$aliases = @('Aviation', 'NC', 'Nielsen', 'NielsenCopy')

./scripts/validate-measure-quality.ps1 `
  -ProjectPaths $paths -Aliases $aliases -Iterations 5 -Warmups 1
```

The command does **not** discover projects, open Power BI Desktop, connect to
remote datasets, refresh data or modify PBIP files. Supply exact `.pbip` files.
Aliases are restricted to 1–48 alphanumeric, underscore or hyphen characters;
do not use sensitive names. The script rejects output paths inside the PBIQ
repository or inside the input project folders.

### What it verifies

For each project it runs `inspect --measures --no-color`, `audit --no-color`
and `audit --quiet` during warmups and measured rounds. It verifies:

1. Inspection totals: `inspected == total == visible + hidden`.
2. Independent format/type category sums equal the visible count.
3. Metadata note subtotals fall within their parent categories.
4. `inspect` and `audit` report identical totals and format counts.
5. SM-002 warnings never exceed all model warnings or missing-format measures.
6. Review count cannot exceed missing + dynamic-format measures.
7. Full and quiet audit agree on exit code, status and finding counts.
8. Aggregates and verdict are **identical between rounds** on an unchanged project.
9. End-to-end median/p95 durations are collected separately for inspection and
   auditing. Timing captures process startup and TOM deserialization.

The script accepts evaluated audit exit codes `0` or `1`: a legitimate quality
finding is not a runner crash. Exit `2` for an attempted audit is a validation
failure. Run it on unchanged local models to test determinism: simultaneous
edits to input TMDL files make repeated results incomparable.

### Data handling

**Only** `share-summary.csv` is persisted, under a new timestamped directory in
`Documents/PBIQ-RealWorld-Audits` by default. It contains only:

- aliases and generic PASS/FAIL reason codes;
- integer model/measure/format/type counts, warnings/review counts;
- audit exit/status, iteration count and aggregate timing statistics.

Raw CLI text is never saved or written to the terminal, including on failures.
The runner holds it in memory only, then discards it. It never writes paths,
model names, table/measure names, data sources, DAX expressions or exception
stacks into the CSV. **Counts themselves may still be commercially sensitive;
review the CSV before sharing it.** Avoid uploading private models, audit
`--details` output, TOM previews, or copies of project folders.

## Acceptance criteria

- `dotnet build -c Release -warnaserror` and `dotnet test -c Release --no-build` pass.
- `dotnet format whitespace --verify-no-changes` and `git diff --check` pass.
- GitHub Actions runs the new validator on **public** valid/warning examples on
  both Windows and Ubuntu.
- All chosen real models report `PASS` in the local CSV, with stable aggregates;
  any genuine failure is investigated without exporting private metadata.
- Real median/p95 latency is compared with earlier 02.4.2 measurements on the
  **same machine**, `dotnet` version and model sources. Avoid hard-coded
  performance thresholds across different machines, OSes or CI runners.
- The Nielsen baseline should continue to show 763 visible measures, 104 static
  formats, 0 dynamic formats, 659 missing formats and 659 review items **only if
  the underlying model has not changed**. Numeric-type coverage remains 0% if
  TOM continues to return Unknown for all 763 measures.

This milestone completes the validation *infrastructure*; validation of private
models cannot be claimed complete until it is executed on the user's machine.
