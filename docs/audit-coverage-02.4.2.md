# 02.4.2: Audit coverage and real-world performance

## Problem

A passing quality audit is not proof that all model objects were checked.
For example, `SM-002` deliberately ignores measures whose offline TOM type
is `Unknown` or `Variant`. Counting only warnings would conceal this gap.

## Object coverage

`pbiq audit <path>` now includes an **OBJECT COVERAGE** block inside the
existing **02 RULE COVERAGE** section in both the styled terminal report
and `--no-color` text. Failure reports without a loaded model do not invent
coverage numbers. Exit codes, rule severities and the four report section
numbers remain unchanged.

| Rule | Counted as checked | Excluded from that rule's scope |
| --- | --- | --- |
| `SM-001` | Active relationships | Inactive relationships |
| `SM-002` | Visible numeric measures with a known `Int64`, `Decimal` or `Double` type | Hidden measures and tables, known nonnumeric measures, unclassified types |
| `SM-003` | Visible columns in visible tables | Hidden columns and all columns in hidden tables |
| `SM-004` | Inactive relationships | Active relationships |

**Checked** means that the rule's simple metadata predicate could be
applied to this object. It does not mean that DAX was evaluated, formatting
was visually verified, filter behavior was tested, or the entire model is
certified. `SM-001`/`SM-004` report their respective relationship subsets
rather than double-counting all relationships as checked.

### SM-002 mutually exclusive buckets

1. **Hidden**: hidden measure or any measure in a hidden table.
2. **Unknown/variant/other type**: blank, `Unknown`, `Variant`, or any TOM
   type not classified by this rule. These are **not** inferred from DAX.
3. **Known nonnumeric**: `String`, `Boolean`, `DateTime`, or `Binary`.
4. **Formatted static**: known numeric measure with nonempty static format.
5. **Formatted dynamic**: known numeric measure with a dynamic format
   definition; this takes precedence over any static fallback.
6. **Unformatted**: known numeric measure with neither formatting option.

The classes exhaust all measures, so:

```
total = hidden + unknown + nonnumeric + static + dynamic + unformatted
visible = total - hidden
numeric checked = static + dynamic + unformatted
formatted = static + dynamic
skipped = hidden + unknown + nonnumeric
```

The SM-002 rule uses the *same classifier* as the coverage analyzer, avoiding
a report that claims a measure was checked when the rule skipped it. The
existing failure policy is unchanged: unknown types do not create a new
Warning or Error. Per-rule coverage is only printed for registered codes
that were actually listed in the completed audit result.

### Example output (illustrative, NOT Nielsen results)

```text
  OBJECT COVERAGE (metadata predicates, not DAX evaluation)
  SM-001  3/5 active relationships checked; 2 inactive excluded
  SM-002  180/760 visible measures checked for numeric formatting (763 total)
          formatted 175 (static 150, dynamic 25); unformatted 5
          excluded: unknown/variant/other type 500; nonnumeric 80; hidden 3
  SM-003  160/172 visible columns checked; 12 hidden excluded
  SM-004  2/5 inactive relationships checked; 3 active excluded
  Coverage is based on TOM metadata; unknown measure types are not inferred from DAX.
```

## Verify on private models

Execute on the local machine only; `audit` does not modify the PBIP source:

```powershell
dotnet clean -c Release
dotnet restore
dotnet format whitespace
dotnet build -c Release -warnaserror
dotnet test -c Release --no-build

dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- audit 'C:\Reports\Private.pbip' --no-color
$LASTEXITCODE
```

**Do not publish raw private console logs, DAX, paths, or PBIP exports.** The
public tests only use synthetic TOM snapshots. The existing real-world
runner (`scripts/real-world-validation.ps1` from 02.4.1) may produce private
logs; keep these outside Git.

## Optional benchmark (PowerShell 7)

Run after building Release. The benchmark executes `dotnet pbiq.dll` with
`audit --no-color` to avoid repeated builds, performs one warm-up, and
measures five end-to-end CLI processes per model by default. This includes
CLI startup and TOM loading. It does **not** isolate just the rule engine.

```powershell
$projects = @('C:\Reports\Aviation\Example.pbip', 'C:\Reports\NC\Example.pbip')
.\scripts\benchmark-real-world.ps1 -ProjectPaths $projects -Aliases @('Aviation','NC') -Iterations 5
```

Only anonymized aliases, median/min/max/p95 timings, count of measured runs,
exit codes and policy are written to `Documents/PBIQ-RealWorld-Audits/`.
Temporary stdout/stderr are discarded. Keep local hardware, caches, file
system and model size in mind when comparing results; these are descriptive
local measurements, not CI pass/fail thresholds.

## Security and limitations

- TOM deserialization is read-only; no Fabric connection and no DAX execution.
- Some measure types reported as unknown may truly be numeric. Coverage
  makes this explicit without guessing.
- The four SM rules are metadata heuristics and do not replace performance
  profiling, data validation or Power BI Desktop verification.
- Model names and DAX expressions are not written by the benchmark script.
- Do not ship private PBIP/TMDL model files or raw audit logs to GitHub.
