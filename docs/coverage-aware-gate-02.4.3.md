# Milestone 02.4.3: Coverage-aware Quality Gate

PBIQ's **SM-002** format check cannot safely infer a measure's result type by
reading an arbitrary DAX expression. The official offline TOM snapshot can
report `Unknown` for a measure's `DataType`, even if the model includes a
format string. `SM-002` deliberately does not guess and must never report
such a measure as checked.

This milestone makes this limitation visible and optionally enforceable.

## CLI usage

```powershell
# Advisory coverage: gaps do not fail by default.
dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- audit examples/valid-pbir-tmdl
$LASTEXITCODE  # 0; potentially PASS WITH COVERAGE GAPS

# Require at least 95% coverage for SM-002, independently of --fail-on-warning.
dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- audit examples/valid-pbir-tmdl --min-measure-coverage 95
$LASTEXITCODE  # 1 when below threshold

# Show ANSI-free output for CI/debug logs.
dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- audit examples/valid-pbir-tmdl --min-measure-coverage 95 --no-color
$LASTEXITCODE
```

`--min-measure-coverage` accepts a whole percentage from `0` to `100`.
Omitting it **does not enforce** a minimum. Invalid values are rejected with
exit code `2` and a diagnostic, before project discovery.

## What is counted

```
checked = visible measures with known numeric TOM type
unknown = visible measures with Unknown / Variant / unsupported TOM type
candidates = checked + unknown
coverage = 100 * checked / candidates
```

`checked` includes known-numeric measures that are formatted **and** those
that are unformatted and already reported by `SM-002`. Known nonnumeric and
hidden measures are excluded from the denominator, not penalized. The
threshold is compared using **integer cross multiplication**, not a rounded
one-decimal display value.

When `candidates == 0`, coverage is **N/A**, not `0%` or `100%`, and no
measure-coverage threshold failure is produced. `SM-002` must be among the
evaluated rules before coverage is claimed. A future policy can explicitly
require the presence of particular rule packs.

## Outcomes and precedence

| Condition | Outcome | Exit |
| --- | --- | --- |
| Rule error | `FAIL` | `1` |
| Warning with `--fail-on-warning` | `FAIL (WARNING POLICY)` | `1` |
| Coverage below specified minimum | `FAIL (COVERAGE POLICY)` | `1` |
| Nonblocking unknown types | `PASS WITH COVERAGE GAPS` | `0` |
| Other advisory findings only | `PASS WITH ADVISORIES` | `0` |
| Clean evaluated rules | `PASS` | `0` |
| No evaluated rules, or input/TOM error | `NOT EVALUATED` / `NOT RUN` | `2` |
| Invalid PBIP structure | `NOT RUN` | `1` |

A coverage gap is a **limitation of the audit**, not proof that the model is
bad. The new status does not change diagnostic severities. Even when both
coverage and warnings require blocking, the warning outcome takes precedence.

## Privacy-safe regression profiles

`SyntheticModelProfileTests` constructs in-memory TOM snapshots from public
counts only. It uses generic names (`Table0`, `Column0_0`, `Measure0_0`),
dummy expressions, and no original source code or business metadata:

| Synthetic profile | Tables | Columns | Measures | Relationships | Scenario |
| --- | ---: | ---: | ---: | ---: | --- |
| Aviation | 16 | 96 | 96 | 8 | all numeric measures classified |
| NC | 159 | 1869 | 120 | 123 | 10 synthetic bidirectional relationships |
| Nielsen | 36 | 172 | 763 | 5 | 763 synthetic unknown measure types |

These are **synthetic scale and policy fixtures**, not anonymized copies of
actual PBIP/TMDL exports; they do not guarantee the same performance or
semantics as actual Power BI Desktop projects.

## Local validation

```powershell
dotnet clean -c Release
dotnet restore
dotnet format whitespace
dotnet build -c Release -warnaserror
dotnet test -c Release --no-build
dotnet format whitespace --verify-no-changes
git diff --check
```

GitHub Actions tests the real `pbiq audit` executable on Windows and Ubuntu,
including policy `0` and `95`, plus an invalid value `101`.

## Current limitation

The threshold measures **classification coverage**, not proof of semantically
correct formatting or DAX correctness. A known numeric measure with missing
format can be fully covered while still emitting an `SM-002` warning.
PBIQ remains offline and read-only.
