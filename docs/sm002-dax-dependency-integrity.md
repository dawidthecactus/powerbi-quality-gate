# SM-002.5a: DAX Dependency Integrity (advisory discovery)

## Motivation

SM-002.4 confirmed 47/47 conclusive sample observations across two private
models; one sampled expression could not execute due to an unresolved reference
in the test model. This illustrates why **type-shaped evidence** and
**executable/valid dependencies** must not be confused. The aim is a read-only,
opt-in *candidate* finder, not a DAX validator or a new gate rule.

## Scope

`DaxDependencyInspector` uses the local TOM inventory, not a Power BI/Fabric
connection. It inspects all defined **measure expressions** (including hidden
measures), looking lexically for:

- `'Table'[Member]` with escaped apostrophes (`''`)
- `Table[Member]` for unquoted simple table identifiers
- `[Name]` with no table qualifier

For each reference it considers:

- **Unknown table qualifier:** the explicit table qualifier is not in the local
  model snapshot.
- **Unknown member in known table:** the qualifier names a TOM table, but the
  bracket name is neither a column nor a measure belonging to that table.
- **Unresolved unqualified [Name]:** the name cannot be found among **any**
  physical TOM measure or column names. DAX can also contain virtual columns,
  so this is the least conclusive category.

Table/column/measure names are matched case-insensitively. The scanner ignores
ordinary DAX text literals (including doubled quotes), comments (`//`, `--`,
`/* ... */`) and escaped bracket/table identifiers. It deduplicates identical
candidates within each measure.

Malformed lexical inputs, unclosed comments/quotes/brackets, expressions over
65,536 characters and expressions with over 512 references are **skipped**
wholly, to avoid reporting partial references. `SkippedExpressions` counts them.

**Not supported:** full DAX AST or binding rules, evaluation contexts,
expression precedence, measure scope, relationships, calculated columns,
calculated tables, calculation groups, field parameters, virtual tables and
columns, external references, or DAX validity itself. In particular,
`ADDCOLUMNS` and `SELECTCOLUMNS` may define bracket names absent from TOM.
A flagged reference is **not necessarily a broken measure**.

## CLI (opt-in only)

Existing `pbiq inspect <path> --measures` behavior is unchanged.

```powershell
# Aggregate-only. Existing model path stays local.
dotnet .\src\PowerBIQualityGate.Cli\bin\Release\net10.0\pbiq.dll `
  inspect $modelPath --measures --dependencies
```

The optional summary contains only constant labels and integer counts:

```text
DAX dependency discovery (advisory; not validated DAX errors):
  Measures inspected: N
  Expressions skipped (unsupported lexical shape/limit): N
  Measures with candidate references: N
  Candidate references: N
    Unknown table qualifiers: N
    Unknown member in known table: N
    Unresolved unqualified [Name]: N
```

The standard `--measures` header can still print a **private PBIP basename**;
only share filtered dependency-summary lines, not the full CLI output.

## Private CSV for resolving suspicious references

Use a new output path **outside all Git repositories**. The exporter rejects
existing files, invalid aliases, non-CSV paths, missing directories and linked
output directories. This report is **identifying and private**.

```powershell
$private = Join-Path $HOME 'PBIQ Pilot Private'
$cli = '.\src\PowerBIQualityGate.Cli\bin\Release\net10.0\pbiq.dll'
$entry = Import-Csv (Join-Path $private 'manifest.csv') |
  Where-Object { $_.case_id -eq 'P01' } | Select-Object -First 1

& dotnet $cli inspect $entry.path --measures --dependencies `
  --dependency-review (Join-Path $private 'sm0025a-P01-dependencies.csv') `
  --case-id P01 | Out-Null
if ($LASTEXITCODE -ne 0) { throw 'Dependency review failed' }
```

Local review CSV columns: `case_id,measure_table,measure_name,reference_table,`
`reference_name,candidate_kind,review_status,notes`. Its object names and
reference labels **must not be published**. No complete DAX expressions,
connection strings, source file paths or format strings are emitted. CSV fields
are quoted and spreadsheet-formula-like values are prefixed with apostrophes.

Mark each candidate locally as true missing dependency, intentional/virtual
name, uncertain or not applicable, after confirming it in an authorized
Power BI Desktop model. **Do not automatically rename any measure or column.**

## Regression and safety invariants

- This pass is entirely read-only, opt-in and advisory. Exit code for a valid
  inspection remains `0` even if candidates are found.
- Quality Gate findings, confirmed SM-002 warnings, TOM classification,
  coverage thresholds, JSON v1 and SARIF are unaffected.
- Published `v0.3.0-alpha.1` remains untouched. No changes to `audit`.
- No private names or snippets appear in the new default console section.
- Tests use only synthetic models, never private customer data.

```powershell
dotnet test -c Release --filter 'FullyQualifiedName~DaxDependencyIntegrityTests'
dotnet test -c Release
dotnet build -c Release --no-incremental -warnaserror
python -m unittest discover -s tests/pilot -p 'test_*.py' -v
git diff --check
```

## Review success criteria

The first private P01/P02 pilot should report only anonymized totals and allow
manual confirmation of at least one known unresolved dependency, with
suspicious virtual-column cases treated as **possible false positives**.
Measure type inference and SM-002 audit counts must be identical before and
after applying this patch. Follow-on SM-002.5b can add a more precise binding
layer *only after* reviewing false-positive sources with synthetic cases.
