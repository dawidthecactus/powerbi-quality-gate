# Milestone 02.4.5.3: SM-002 evidence-based rule refactor

This milestone refactors the existing **SM-002 numeric-measure formatting rule**
using the metadata inventory added in milestones 02.4.5.1 and 02.4.5.2.
It does **not** alter the console layout, add new rule codes, execute DAX,
or change the meaning of `--min-measure-coverage`.

## Motivation

In a private, real-world Nielsen model, the offline Microsoft TOM reader
reported 763 visible measures with `DataType = Unknown`. The new metadata
inventory independently found **104 static formats**, **0 dynamic formats**
and **659 missing formats**. A missing format alone cannot establish that an
unknown-type measure is numeric, so turning all 659 into warnings would be
misleading.

## Decision table

| TOM type evidence | Formatting evidence | SM-002 assessment | Finding |
| --- | --- | --- | --- |
| Known numeric | No usable static or dynamic definition | `KnownNumericMissingFormat` | Warning |
| Known numeric | Static or dynamic definition with nonblank expression | `KnownNumericWithFormat` | None |
| Known nonnumeric | Any | `KnownNonNumeric` | None |
| Unknown / Variant | Format present | `UnknownTypeWithFormat` | None |
| Unknown / Variant | Format missing | `UnknownTypeMissingFormat` | Review only |
| Unsupported future type | Format present or missing | `UnsupportedType*` | Review only if missing |
| Numeric or unclassified | Dynamic definition with blank/unavailable expression | `DynamicFormatNeedsReview` | Review only |
| Hidden measure or hidden table | Any | `Hidden` | None |

Review items are **not** audit findings, and are **not** interpreted as
confirmed errors. A static fallback alongside an unverified dynamic format
also remains a review case; the tool does not decide precedence by executing
DAX. The metadata inventory still records those two definitions independently.

SM-002 continues to warn only when the TOM type is known numeric and no
format definition is present. A dynamic definition whose expression is
unavailable is not treated as definitely absent. This intentionally favors
fewer false positives over speculative diagnoses.

## API and responsibilities

- `MeasureInspectionEngine` reads all measures and normalizes format/type
  evidence without interpreting DAX.
- `MeasureFormattingAssessmentEngine` partitions every measure into a
  mutually exclusive assessment category, exposing per-measure records and an
  aggregate `MeasureFormattingAssessmentSummary`.
- `UnformattedNumericMeasureRule` derives **warnings only** from
  `KnownNumericMissingFormat`.
- `ModelAuditCoverageAnalyzer` uses the same normalized inspection evidence
  for existing SM-002 type-classification counters and exposes the aggregate
  assessment via `ModelAuditCoverage.Sm002Assessment`.

`Summary.AccountedFor == Summary.Total` holds, including hidden objects.
`Summary.NeedsReview` counts ambiguous missing formats and unverified dynamic
expressions. No DAX expressions or original format strings are included in
assessment output, and only names (not expressions) are present in individual
assessment records for possible future opt-in details.

## Compatibility and release boundary

- There are still four default rules, `SM-001` through `SM-004`.
- `--min-measure-coverage` still measures **numeric type classification**:
  `checked numeric / (checked numeric + unknown or unsupported)`. It is not
  a threshold for the existence or semantic correctness of format metadata.
- `PASS WITH COVERAGE GAPS` and exit codes `0/1/2` retain their behavior.
- Default audit, `--details`, `--quiet`, `--no-color` and CI output are
  unchanged. Review counts are available in the Core API for milestone
  **02.4.5.4**, where they can be surfaced without a wall of text.

## Synthetic Nielsen-scale regression

The regression constructs 763 generic in-memory measures:
104 unknown-type measures with static formats and 659 unknown-type measures
without a format. Expected: 104 `UnknownTypeWithFormat`, 659
`UnknownTypeMissingFormat`, **0 SM-002 warnings**, default nonblocking
coverage gaps and the existing strict coverage gate failure at 95%.

This is a synthetic fixture containing no private PBIP, column names,
real measure expressions, or model exports. It verifies behavior and size,
not the real Nielsen model's business semantics.

## Validate locally

```powershell
dotnet clean -c Release
dotnet restore
dotnet format whitespace
dotnet build -c Release -warnaserror
dotnet test -c Release --no-build
dotnet format whitespace --verify-no-changes
git diff --check

$nielsen = "$env:USERPROFILE\Desktop\NCcostam\Joanna Kieckarz\NIELSEN\NielsenTEST.pbip"
dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- inspect $nielsen --measures --no-color
dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- audit $nielsen --quiet
$LASTEXITCODE
```

Do not commit private model data or full detailed logs. The inspection
summary and synthetic aggregates are safe to share.
