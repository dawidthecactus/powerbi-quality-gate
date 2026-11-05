# 02.3.1: Semantic model quality rules engine

## Goal and scope

Milestone 02.3.1 adds a **pure, read-only rule execution layer** under
`PowerBIQualityGate.Core.Quality`. It takes the `TomModelSnapshot` introduced
in 02.2 and evaluates registered `IModelQualityRule` implementations.

**No actual quality checks are shipped at this milestone.** They arrive in
02.3.2. No new `audit` CLI command or terminal renderer is added yet.
Existing `scan` and `inspect` behavior is unchanged.

## Public API

| Type | Purpose |
| --- | --- |
| `IModelQualityRule` | Stable code, title, severity and an `Evaluate` method |
| `ModelQualityIssue` | Object location, message, impact and remediation returned by one rule |
| `ModelQualityFinding` | Issue enriched with its registered rule's metadata |
| `ModelQualityAuditEngine` | Validates rules, executes them and orders findings |
| `ModelQualityAuditResult` | Audited model name, evaluated rule codes, findings and counts |
| `ModelQualityRuleExecutionException` | Fail-closed exception carrying the failing rule's code |
| `QualityAuditVerdict` | `NotEvaluated`, `Passed`, `Failed` |

Codes are reserved as `SM-001` to `SM-999` (three decimal digits after
`SM-`). A code must be unique within a configured engine. Rule execution
order is ascending code order, independently of registration order.

Findings are ordered by severity (Error, Warning, Information), then code,
location, and message. The result exposes read-only collections.

### Example rule (not part of the shipped rule pack)

```csharp
using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;
using PowerBIQualityGate.Core.Validation;

public sealed class ExampleRule : IModelQualityRule
{
    public string Code => "SM-001";
    public string Title => "Example metadata check";
    public DiagnosticSeverity Severity => DiagnosticSeverity.Information;

    public IReadOnlyList<ModelQualityIssue> Evaluate(TomModelSnapshot model) =>
        model.Tables.Count == 0
            ? [new ModelQualityIssue(
                model.Name,
                "The model contains no tables.",
                "An empty model may be an unfinished export.",
                "Review the expected tables in Power BI Desktop.")]
            : [];
}

var engine = new ModelQualityAuditEngine([new ExampleRule()]);
ModelQualityAuditResult result = engine.Run(snapshot);
```

## Audit semantics

- No configured rules => `NotEvaluated`, **not** `Passed`.
- At least one rule, no Error findings => `Passed` by default, including
  warnings or information findings. Later CLI policy options may implement
  warning-sensitive exit codes.
- At least one Error finding => `Failed`.
- A rule throwing an exception, returning null or emitting an incomplete
  issue => `ModelQualityRuleExecutionException`. The engine never treats
  a failed rule as a clean audit.
- Missing rule titles, malformed/duplicate codes or invalid severities
  are rejected during engine construction.
- Rules may only inspect the snapshot. The engine does not deserialize TMDL,
  access the filesystem, or write back metadata. TOM/DAX validity and
  Power BI refresh are outside this milestone's guarantees.

## Verification

```powershell
dotnet restore
dotnet build -c Release -warnaserror
dotnet test -c Release
dotnet format whitespace --verify-no-changes
git diff --check
```

All tests for this milestone use stub rules and synthetic `TomModelSnapshot`
objects. They verify verdicts, rule registration, sorting, guidance,
exception behavior and repeated runs without dependence on a Power BI server.

## Next milestone

**02.3.2: First Rule Pack.** Implement `SM-001` through `SM-004` against TOM
snapshots (relationship direction, measure formatting, technical column
visibility, inactive relationships). The audit CLI and shared branded
rendering will be delivered separately after the core checks are stable.
