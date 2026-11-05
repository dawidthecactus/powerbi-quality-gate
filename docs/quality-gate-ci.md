# 02.3.4: Policy-based quality gates and cross-platform CLI smoke tests

## What changes

The default `pbiq audit <project>` remains **advisory**: Error findings block,
Warning and Information findings do not. The optional `--fail-on-warning` switch
makes **at least one Warning** a blocking finding without changing the rule's
severity, its advice, or the model metadata.

The pure `QualityGatePolicy` evaluates a completed `ModelQualityAuditResult`.
`AuditCommand` applies that policy once and routes successful audits to stdout,
blocking/failing audits to stderr. Both renderers receive the same
`QualityGateDecision`, so text, colors and process exit code agree. No TOM
objects are changed or saved, and the command never executes DAX.

## Exit-code contract

| Exit code | Meaning |
| --- | --- |
| `0` | Rules ran, no Error findings; Warning findings allowed when strict policy is off. Information is allowed. |
| `1` | Quality gate blocked by Error findings, or by Warning findings with `--fail-on-warning`; also used for structural PBIP validation failure before the audit. |
| `2` | Missing/ambiguous input, unsupported model format, TOM deserialization failure, rule execution failure, or no evaluated rules. These are **not** passing audits. |

The CLI deliberately displays `AUDIT: FAIL (WARNING POLICY)` when the strict
policy rejects warnings, and keeps the original `Warning` severity visible.
The same invocation without strict policy displays `PASS WITH ADVISORIES`.

> The quality gate only covers the **four shipped metadata rules**. It does not
> prove DAX, relationship cardinality, query performance, refresh, or semantic
> correctness. A `PASS` result is not a model certification.

## Command-line examples

From the repository root after `dotnet build -c Release`:

```powershell
dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- audit examples/valid-pbir-tmdl
$LASTEXITCODE # 0: information-only baseline

dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- audit examples/warning-pbir-tmdl
$LASTEXITCODE # 0: warning is advisory by default

dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- audit examples/warning-pbir-tmdl --fail-on-warning
$LASTEXITCODE # 1: warning triggers policy failure

dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- audit examples/valid-pbir-tmdl --fail-on-warning --no-color
$LASTEXITCODE # 0: informational findings do not fail strict policy

dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- audit examples/invalid-pbip-shortcut --fail-on-warning
$LASTEXITCODE # 1: structural error, audit NOT RUN
```

### Running within your own GitHub Actions workflow

Use the built `pbiq.dll` as an ordinary .NET process. Replace the example path
with your checked-in PBIP folder:

```yaml
- name: Quality gate for PBIP
  shell: pwsh
  run: |
    dotnet build -c Release -warnaserror
    dotnet src/PowerBIQualityGate.Cli/bin/Release/net10.0/pbiq.dll audit path/to/MyProject --fail-on-warning --no-color
    if ($LASTEXITCODE -ne 0) { throw 'Power BI quality gate failed' }
```

Run build/test/format in your CI as appropriate; the excerpt illustrates the
quality gate itself. CI can upload or retain redirected stdout/stderr as logs.
`--no-color` is recommended for deterministic log output.

## Synthetic warning fixture

`examples/warning-pbir-tmdl` is a **separate, synthetic copy** of the small
TMDL sample with `crossFilteringBehavior: bothDirections` added to the
`Sales[ProductId]` to `Product[ProductId]` relationship. It deliberately
triggers `SM-001` (Warning) and `SM-003` (Information). No business data or
user-owned project is included. This is only a demonstration of rule behavior,
not a production-ready or refreshable model.

`SM-002` can skip numeric measures with `Unknown` TOM data types, especially in
offline metadata-only deserialization. `SM-004` is covered by unit tests for
inactive relationships. The warning fixture is not intended to trigger every
rule.

## Regression coverage

- Unit tests cover default, strict, informational, error and not-evaluated
  verdicts without modifying finding severity.
- Renderer tests cover color and `--no-color` output when warnings block.
- Command tests use the real discovery/structure/TOM/rule pipeline with both
  the baseline and the warning TMDL fixtures, including stderr behavior.
- The **existing** GitHub Actions matrix (Windows and Ubuntu) additionally
  executes six **actual CLI processes** with expected exit codes `0`, `1`, `2`
  and asserts that a matching diagnostic appears on stdout/stderr.
- Input errors are not reclassified by the `--fail-on-warning` flag.

```powershell
dotnet clean -c Release
dotnet restore
dotnet build -c Release -warnaserror
dotnet test -c Release --no-build
dotnet format whitespace --verify-no-changes
git diff --check
```

When installing ZIP overlays, use the clean rebuild above if old compiled
DLL timestamps cause incremental build results to look stale.

## Limitations and future work

The strict policy uses a single threshold switch. Rule-by-rule suppression,
configurable profiles, SARIF and JSON exports, DAX analysis, full PBIP schema
validation, and a hardened build/test release remain separate future work.
