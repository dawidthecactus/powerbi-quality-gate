# PBIQ v0.3.0: Audit JSON diagnostic contract (03.1)

## Status

**Experimental v0.3 source contract, implemented across stages 03.1–03.6.** The public
[`v0.2.0-rc.1` release](https://github.com/dawidthecactus/powerbi-quality-gate/releases/tag/v0.2.0-rc.1)
does **not** include `--format json`. Branch CI alone is not a release: check the verified GitHub tag,
standalone distributions and external consumer integration separately.

The initial feature extends **`audit` only**; `scan` and `inspect` remain
text-only. Do not advertise JSON support in public release notes yet.

## Usage

```powershell
# From the checked-out v0.3 feature branch, after dotnet build -c Release:
dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- audit examples/valid-pbir-tmdl --format json

dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- audit examples/warning-pbir-tmdl --fail-on-warning --format json
```

`--format text` remains the original output mode (also the default). JSON is
always one well-formed, pretty-printed document on **stdout**, including when
`exitCode` is `1` or `2` after the `audit` handler runs. The process exit code
still controls CI success/failure. JSON has **no ANSI escape sequences**.

**Limits:** failures produced by the System.CommandLine parser *before the
handler is invoked* (e.g. missing required `<path>`, incomplete `--format`
argument, unknown option) use the parser's standard text error, not a JSON
document. A production integration should check the process exit code and
handle invalid or empty stdout accordingly.

Combining JSON with `--details` or `--quiet` is rejected with a JSON failure
object and exit `2`. `--no-color` is accepted but unnecessary for JSON.
Unsupported values such as `--format xml` produce a text error and exit `2`.

## Example status envelope

An abbreviated valid public-fixture output (some diagnostics omitted):

```json
{
  "schemaVersion": "1.0.0",
  "tool": { "name": "pbiq", "version": "0.3.0-alpha.1" },
  "command": "audit",
  "project": {
    "name": "Example.pbip", "modelName": "Example",
    "tables": 2, "measures": 2, "relationships": 1
  },
  "result": { "status": "pass_with_coverage_gaps", "exitCode": 0 },
  "summary": { "rulesEvaluated": 4, "errors": 0, "warnings": 0, "information": 3 },
  "policy": { "failOnWarning": false, "minimumMeasureCoverage": null },
  "coverage": { "measures": {
    "inspected": 2, "visible": 2, "checkedNumeric": 0, "candidates": 2,
    "unknownType": 2, "needsReview": 2, "classificationPercent": 0,
    "hasGaps": true
  } },
  "rules": ["SM-001", "SM-002", "SM-003", "SM-004"],
  "diagnostics": [],
  "failure": null
}
```

The JSON block is **illustrative**, not a complete copied CLI report: the
real public fixture currently returns three informational diagnostics. The
actual `tool.version` may include build metadata (`+<commit>`).

## Contract

The machine-readable contract is [`docs/schemas/audit-report.v1.schema.json`](schemas/audit-report.v1.schema.json),
written against JSON Schema draft 2020-12. Its top-level fields are always
present, even on failure:

| Property | Meaning |
| --- | --- |
| `schemaVersion` | Contract identifier, independent of CLI SemVer |
| `tool`, `command` | CLI identity and audit command |
| `project` | Base PBIP filename, TOM model name and object counts; counts may be `null` on failure |
| `result.status` | Stable status identifier (snake_case), separate from exit code |
| `result.exitCode` | 0 accepted, 1 rejected by gate, 2 not evaluated |
| `summary` | Rule and finding counts, not a claim of full semantic correctness |
| `policy` | Actual warning and classification-coverage thresholds |
| `coverage` | SM-002 counts, or `null` if evaluation did not complete |
| `rules` | Evaluated rule IDs, ordered by the engine |
| `diagnostics` | Structured findings with severity, object location and remediation |
| `failure` | `null` on completed evaluation, code/message on incomplete audits |

### Status identifiers

- `pass`, `pass_with_advisories`, `pass_with_coverage_gaps`
- `fail`, `fail_warning_policy`, `fail_coverage_policy`
- `not_evaluated`

### Finding locations and privacy

`diagnostics[].location.objectPath` is a TOM object identity, **not** a source
file. Stage 03.2 indexes verified TMDL declarations for supported SM rules.
`filePath` is slash-normalized and relative to the PBIP project directory
(or containing Fabric workspace), and `line` is
1-based. Both remain `null` if a declaration is missing, ambiguous, or outside
the project directory. PBIQ does not synthesize source coordinates from DAX,
relationship endpoints, or object names alone. For a discovery failure, only
the base file name is included in `project.name`, not the input directory.

JSON intentionally excludes DAX expressions, raw TOM objects and source data.
It **can contain measure/column names and diagnostic messages**, so review
its contents before sharing outside your organization. Parser errors and
model metadata may include sensitive information in other terminal modes.

Unknown/variant measure types remain **needs-review items**, not confirmed
SM-002 warnings. The JSON renderer never reclassifies measures.

## Compatibility promise

During this alpha period, contract version `1.0.0` is a **design baseline**,
not a published stable API. After stabilization, consumers should validate
`schemaVersion` before decoding. Future additive v1 fields may be introduced
with a compatible version policy; breaking changes require a separately
versioned v2 schema.

The JSON renderer is a separate presentation layer: it does not modify the
rule engine, the coverage policy, the verdict, or the existing text rendering.
Tests cover success, warning failure, coverage failure, missing project,
privacy boundaries, determinism and no fabricated source coordinates.

## Next stages

- **03.2:** TMDL source-coordinate mapping and path normalization (implemented for SM rules; PBIR mapping is not included).
- **03.3:** `.pbiq.yml` policy config, rule enable/disable and exact object exclusions (implemented; no `--exclude` CLI option).
- **03.4:** SARIF export and GitHub annotation support (implemented).
- **03.5:** reusable pinned GitHub Action (implemented).
- **03.6:** native Fabric Git `.SemanticModel` directories without `.pbip` shortcut (implemented).
- **03.7:** release readiness, documentation and standalone distribution smoke checks.

## Configuration addendum (03.3 alpha)

The `summary.suppressed` nonnegative integer reports how many findings were
removed by explicit object exclusions. The JSON contract remains 1.0.0 during
alpha, with a documented additive field and updated JSON Schema. Missing
configuration preserves the former default behavior. See
[milestone 03.3](milestone-03.3-configuration-engine.md).
