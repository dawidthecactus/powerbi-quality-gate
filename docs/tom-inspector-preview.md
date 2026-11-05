# 02.2: Microsoft TOM/TMDL inspector (opt-in preview)

## Goal

`pbiq inspect --tom <project>` deserializes local TMDL metadata with the official
`Microsoft.AnalysisServices` package (`19.118.3`) and projects the resulting TOM
objects into read-only snapshot records. No connection to the Power BI Service,
Fabric, or an Analysis Services server is required.

The existing `pbiq inspect <project>` still uses the 02.1 text-declaration
inventory. Keep this as a baseline until the TOM path has passed real-world
project tests on both Windows and Ubuntu. `pbiq scan` is unchanged.

## Usage

```powershell
dotnet run --project src/PowerBIQualityGate.Cli -- inspect examples/valid-pbir-tmdl --tom
$LASTEXITCODE  # expected: 0 when Microsoft TOM deserializes the synthetic fixture

dotnet run --project src/PowerBIQualityGate.Cli -- inspect examples/valid-pbir-tmdl
# 02.1 preliminary declaration counter
```

The TOM preview displays:

- Model name and database compatibility level.
- Table, column, measure, relationship, hierarchy and partition counts.
- Table names, hidden flags, data type and column kind.
- A truncated **display-only** preview of each measure's DAX expression.
- Relationship endpoints, active/inactive flags, and cross-filter direction.

All screen sections use the shared `PbiqConsoleChrome` branding. To prevent
huge reports, table/column/measure/relationship lists show at most 15 entries
and display the number omitted. The full snapshot still contains all objects.

## Integrity and scope

- This uses Microsoft TMDL **deserialization**, not a DAX parser or engine.
  Successful deserialization does not prove that DAX expressions evaluate,
  relationships are sensible, queries execute or Power BI can refresh the data.
- The source folder is read only: never call `SaveChanges`, serialize back to
  the folder, or connect to a server during inspection.
- TOM deserialization may reject a folder accepted by the simple structural
  scanner. Report such errors without falling back silently to raw counting.
- Failure modes: project discovery returns exit code `2`, failed structural
  validation returns `1`, unsupported model formats and TOM parse errors return
  `2`, and a successful inspection returns `0`.
- The synthetic example is deliberately small; it does not include refreshable
  partitions and is not guaranteed to open in Power BI Desktop.
- `Microsoft.AnalysisServices` is Microsoft-licensed and increases release
  artifact size; check package licensing and self-contained Windows/Linux
  publish output before the next public release.

## Quality checks

```powershell
dotnet restore
dotnet build -c Release -warnaserror
dotnet test -c Release
dotnet format whitespace --verify-no-changes
git diff --check
```

The new integration tests exercise the Microsoft serializer against the
synthetic TMDL fixture. If parsing fails, inspect the actual exception and
fix the fixture or projection; do not weaken the TOM test to make it pass.

## API references

- Microsoft TMDL overview: https://learn.microsoft.com/en-us/analysis-services/tmdl/tmdl-overview
- Microsoft `TmdlSerializer`: https://learn.microsoft.com/en-us/dotnet/api/microsoft.analysisservices.tabular.tmdlserializer
- NuGet version: https://www.nuget.org/packages/Microsoft.AnalysisServices/19.118.3

## Next iteration

Validate a small set of representative Power BI Desktop exports; then make
TOM the default inspector engine. Support structured JSON output and a
rule catalog separately, after the inventory data model stabilizes.
