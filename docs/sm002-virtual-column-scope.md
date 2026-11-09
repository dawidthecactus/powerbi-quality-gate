# SM-002.5b: Virtual Column Scope Resolution (opt-in, advisory)

## Motivation

SM-002.5a reported 29 candidate references on two private PBIP models after a
`RETURN [Measure]` lexical fix. In offline manual review, 18 candidates appeared
associated with `ADDCOLUMNS` aliases (a hypothesis to be tested, not 18 proven
false positives). SM-002.5b adds *narrow, context-aware* recognition of virtual
column names, without excluding those names globally.

## Algorithm and trust boundary

For each measure, the existing `DaxDependencyInspector` still collects physical
TOM references. A new bounded lexical pass, `DaxVirtualColumnScopes`, produces
offsets of unqualified `[Name]` tokens that are demonstrably inside a supported
row-expression argument of a table containing a virtual alias:

- `ADDCOLUMNS(table, "Alias", scalar, ...)` produces aliases and inherits those
  of its input table.
- `SELECTCOLUMNS(table, "Alias", scalar, ...)` produces new aliases, not the
  prior table's complete column set.
- `FILTER`, `TOPN`, and `CALCULATETABLE` preserve aliases of their table input.
- Row expressions of `FILTER`, `TOPN`, `ADDCOLUMNS`, `SELECTCOLUMNS`, and
  `SUMX` / `MAXX` / `MINX` / `AVERAGEX` / `COUNTX` / `COUNTAX` /
  `PRODUCTX` / `CONCATENATEX` / `RANKX` can reference aliases of their
  explicit table input (restricted argument positions, no broad keyword match).
- Local `VAR name = table_expression ... RETURN expression` declarations bind
  table aliases to variables **after** their initializer. Aliases can flow
  through consecutive `VAR` assignments, `FILTER`, and `TOPN`.

Only the exact **unqualified bracket token at a proven row-scope offset** may be
excluded from `UnresolvedUnqualifiedReference`. `'Table'[Name]` and
`Table[Name]` are never suppressed. The same alias outside its row context,
inside another independent measure, or in the definition of its own
`ADDCOLUMNS` column remains a candidate.

Safety limits: at most 65,536 DAX characters, 8192 tokens, 128 function
arguments, 128 aliases and 32 nested traversals. Malformed, unsupported,
unknown-source or overly complex expressions fail closed: no new suppression.
The existing inspector's malformed-expression skip behavior is unchanged.

This **is not a full DAX parser**: nested row contexts, complex alias lineage,
calculation groups, `SUMMARIZE` or `GROUPBY` extension columns, inferred
columns from `ROW`/`DATATABLE`, table variables returned by arbitrary scalar
functions, function-specific implicit row contexts and dynamic name resolution
are not guaranteed. A reduced candidate count is not proof that the model is
semantically valid. No data or DAX is executed. Some false positives remain.

## CLI and compatibility

No new CLI flags. Existing opt-in command remains:

```powershell
$private = Join-Path $HOME 'PBIQ Pilot Private'
$cli = '.\src\PowerBIQualityGate.Cli\bin\Release\net10.0\pbiq.dll'
foreach ($entry in (Import-Csv (Join-Path $private 'manifest.csv') |
    Where-Object { $_.case_id -in @('P01','P02') })) {
  $outCsv = Join-Path $private "sm0025b-$($entry.case_id)-dependencies.csv"
  & dotnet $cli inspect $entry.path --measures --dependencies `
    --dependency-review $outCsv --case-id $entry.case_id | Out-Null
  if ($LASTEXITCODE -ne 0) { throw 'Dependency inspection failed' }
}
```

The CSVs remain **private identifying artifacts** outside Git, containing
object names. Do not publish them. The plain `inspect --measures` output also
contains a private PBIP basename. Share only filtered *anonymized count lines*
as in previous pilots. No source DAX or customer information belongs in Git.

`scan`, `audit`, SM-002 warning policy, strict gating, exit codes, JSON v1 and
SARIF are unaffected. The published `v0.3.0-alpha.1` remains immutable.

## Test and acceptance plan

1. Unit tests verify inline and `VAR`-backed table aliases through `FILTER`,
   `TOPN` and common row iterators.
2. Negative tests prove an alias does **not** suppress unknown references in
   another scope, initializer, branch, table, measure or qualified reference.
3. Run full .NET tests and warnings-as-errors build on Windows, plus all Python
   pilot tests. No private source appears in test fixtures.
4. Run P01/P02 against the *same* source versions as SM-002.5a, exporting
   new local CSVs, and compare the aggregate candidate counts against
   P01=15 and P02=14. A reduction is expected, but not guaranteed.
5. Manually review any remaining candidate names locally before concluding
   whether they are genuine missing dependencies.

## Further work

SM-002.5c can add alias-producing `SUMMARIZE` / `GROUPBY`, nested variable
blocks, and verified reference provenance, but only with dedicated synthetic
unit tests and independent private-model evidence.
