# 02.3.2: First semantic-model quality rule pack

Milestone 02.3.2 ships four **advisory, metadata-only** quality rules running on
`TomModelSnapshot` objects returned by the official Microsoft TOM serializer.
It does not introduce a CLI command or change `scan` / `inspect` output.
The rules will be wired to `pbiq audit` in milestone 02.3.3.

## Rules and conservative scope

| Code | Rule | Severity | Trigger | Intentional skips |
| --- | --- | --- | --- | --- |
| `SM-001` | Review bidirectional relationships | Warning | Active relationship with `CrossFilterDirection == BothDirections` | Inactive relationships (reviewed by SM-004); `OneDirection` and `Automatic` |
| `SM-002` | Format visible numeric measures | Warning | Visible measure in a visible table, with a **known numeric TOM data type** (`Int64`, `Decimal`, `Double`), no nonempty static format string and no dynamic format-string definition | Hidden objects, nonnumeric types, **Unknown/Variant** types and dynamically formatted measures |
| `SM-003` | Review visible technical columns | Information | Visible column in a visible table with a conservative ID/Key naming pattern | Hidden objects and ordinary names such as `Paid`, `Grid` or `Identifier` |
| `SM-004` | Review inactive relationships | Information | Any relationship with `IsActive == false` | Active relationships |

These are **review suggestions, not proof of a modeling defect**. Bidirectional
relationships can be necessary. Inactive relationships are legitimate in
role-playing dimensions and with `USERELATIONSHIP`. A visible technical key may
be useful for a specific report requirement.

`SM-003` recognizes exact `ID`/`Key`/`GUID`, `FK_` / `PK_` prefixes, and
common suffixes such as `ProductId`, `OrderID`, `CustomerKey`, `_id`, `_key`,
` ID` or ` Key`. It does not perform dependency analysis.

### Numeric measure limitation

The 02.2 TOM snapshot did not carry a measure's data type or dynamic-format
information. This release extends the existing `TomMeasureSnapshot` with two
**optional trailing parameters**:

```csharp
string DataType = "Unknown",
bool HasDynamicFormatString = false
```

Existing four-argument constructors continue to work. `TomModelReader` now
maps `Measure.DataType` and the presence of `Measure.FormatStringDefinition`
into those fields. Offline TOM deserialization may return `Unknown` for a
measure whose DAX has not been evaluated. **SM-002 skips it on purpose**:
inferring numeric type from the expression text would be unreliable. This
means the rule can have false negatives, and the audit makes no claim to check
formatting for every measure.

A format string defined only at report/visual level is not detected. The rule
checks semantic-model metadata alone.

## Running the pack from code

```csharp
using PowerBIQualityGate.Core.Inspection;
using PowerBIQualityGate.Core.Quality;

TomModelSnapshot snapshot = new TomModelReader().Read(definitionFolder);
var engine = new ModelQualityAuditEngine(ModelQualityRulePack.CreateDefault());
ModelQualityAuditResult audit = engine.Run(snapshot);

Console.WriteLine($"Rules executed: {audit.RulesEvaluated}");
Console.WriteLine($"Warnings: {audit.WarningCount}");
Console.WriteLine($"Information: {audit.InformationCount}");
foreach (var finding in audit.Findings)
{
    Console.WriteLine($"{finding.RuleCode} | {finding.Location} | {finding.Message}");
    Console.WriteLine($"Why: {finding.WhyItMatters}");
    Console.WriteLine($"Fix: {finding.HowToFix}");
}
```

`ModelQualityRulePack.CreateDefault()` returns a fresh, read-only collection
of four rules. Existing `ModelQualityAuditEngine` sorts them by code, validates
results and orders the findings. No rule connects to Power BI Service, changes
metadata, executes DAX, refreshes data, or writes to disk.

## Test coverage

The first pack includes data-driven tests for relationship direction,
relationship activity, numerical measure types and formatting, hidden
objects, technical-column naming false positives, remediation metadata,
repeatable runs, and an integration test using the included synthetic TMDL
fixture via `TomModelReader`.

```powershell
dotnet restore
dotnet format whitespace
dotnet build -c Release -warnaserror
dotnet test -c Release
dotnet format whitespace --verify-no-changes
git diff --check
```

Until a CLI audit command is implemented, validate the rules with tests. Do
not expect any new `pbiq audit` command or model-health section in `inspect`.

## References

- [Relationship filtering and performance guidance](https://learn.microsoft.com/en-us/power-bi/transform-model/desktop-relationships-understand)
- [Active and inactive relationship guidance](https://learn.microsoft.com/en-us/power-bi/guidance/relationships-active-inactive)
- [Microsoft TOM Measure metadata](https://learn.microsoft.com/en-us/dotnet/api/microsoft.analysisservices.tabular.measure)
- [Formatting at model versus visual level](https://learn.microsoft.com/en-us/power-bi/create-reports/desktop-custom-format-strings)

## Next

Milestone **02.3.3** will expose this pack through `pbiq audit` with the
shared PBIQ terminal chrome, diagnostics, and Why/How guidance. CLI exit-code
policy and regression hardening follow in 02.3.4.
