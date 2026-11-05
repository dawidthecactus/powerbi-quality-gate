# Milestone 02.4.5.4: Measure Quality Coverage Integration

## Purpose

Display the aggregate output of the evidence-based SM-002 assessment engine in
`pbiq audit`, without conflating **format metadata inspection** with the
**numeric-type coverage** already enforced by the quality gate.

The default report adds just two lines below the rule rows:

```text
  MEASURE QUALITY  ·  763/763 inspected (763 visible)
    Formats: 104 static · 0 dynamic · 659 missing  |  SM-002: 0 warnings · 659 review

  SM-002 coverage: 0.0% (0/763 candidates) · 763 unclassified
  Coverage gap: numeric applicability unverified for unclassified measure types
```

These counts describe a private Nielsen model only in the aggregate and are
included solely as an example. No business data, DAX expression, or object
names are embedded in fixtures or printed in the summary.

### Meaning of the quantities

- `inspected` counts all measures (including hidden). The format partition is
  **visible** measures only. Thus `static + dynamic + missing == visible`.
- `SM-002 warnings` counts **known numeric** measures with no usable format.
  Other rules can report warnings independently.
- `review` counts unknown/unsupported-type measures lacking a format and
  blank/unavailable dynamic definitions. A review item is NOT an audit finding.
- `SM-002 coverage` and `--min-measure-coverage` continue to mean numeric-type
  eligibility: `checked known numeric / (known numeric + unknown/unsupported)`.
  A 100% format-inventory read must never be presented as 100% type coverage.
- `formatted` means a format definition is present; PBIQ does not validate its
  semantic correctness, and does not parse or execute DAX.

### Output modes

| Mode | Behavior |
| --- | --- |
| `audit <path>` | Two new aggregate measure-quality lines, no object names |
| `audit <path> --details` | Same overview and nonblocking category breakdown, plus existing grouped findings |
| `audit <path> --quiet` | Original, single plain-text status line: unchanged |
| `audit <path> --no-color` | Same counts without ANSI or Spectre markup |

The detail breakdown includes known numeric missing-format warnings, unknown
or unsupported types without a format, dynamic format definitions needing
review, types unresolved despite format evidence, and exclusions. It does not
list measure objects for review items, even in `--details`. Existing confirmed
SM-002 warnings retain their ordinary finding locations in the detailed mode.

A completed audit still routes exit `0` output to stdout and failed audits to
stderr. No new findings, new rule codes, CI thresholds, or severity changes.

### Architecture

`ModelAuditCoverageAnalyzer` already performs one `MeasureInspectionEngine`
pass and builds `Sm002Assessment`. It now also retains the aggregate inspection
summary (`Sm002Inspection`). `AuditCommand` forwards the same coverage object
to `CompactAuditRenderer` after the gate decision is calculated. The renderer
only formats precomputed aggregate counts. For backwards compatibility,
renderer calls without an explicit coverage object still work but omit the
new measures section.

### Validation

```powershell
dotnet clean -c Release
dotnet restore
dotnet format whitespace
dotnet build -c Release -warnaserror
dotnet test -c Release --no-build
dotnet format whitespace --verify-no-changes
git diff --check

dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- audit examples/valid-pbir-tmdl --no-color
dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- audit examples/valid-pbir-tmdl --details --no-color
dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- audit examples/valid-pbir-tmdl --quiet
```

Do not upload real confidential PBIP/TMDL models or detailed finding logs to
GitHub. This milestone uses public examples and synthetic fixtures only.
