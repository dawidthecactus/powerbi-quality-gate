# Milestone 03.3: Configuration engine (alpha)

Status: proposed patch for v0.3; **compile and test on Windows/Linux** before merging.

## Purpose

Enable repeatable team-specific lint settings without changing the four quality
rules, TOM, source-coordinate resolver, gate exit codes or JSON contract v1.

## Discovery and flags

- With no `.pbiq.yml`, audits behave as before.
- PBIQ looks for `.pbiq.yml` **only beside the discovered `.pbip`**. It does not
  climb parent directories or use `cwd` as an implicit fallback.
- `audit <pbip> --config path/to/config.yml` explicitly overrides discovery.
  A missing explicit file fails with exit 2.
- `--fail-on-warning` forces `true` when present; otherwise the file's
  `policy.failOnWarning` is used, defaulting to `false`.
- `--min-measure-coverage 95` overrides `policy.minMeasureCoverage`, which
  is `null` by default (no threshold).

Example YAML (see `examples/pbiq.example.yml`):

```yaml
version: 1
rules:
  SM-001: true
  SM-002: true
  SM-003: true
  SM-004: true
policy:
  failOnWarning: true
  minMeasureCoverage: 95
exclusions:
  - rule: SM-003
    object: Sales[ProductId]
```

## Rules and exclusions

- Setting `rules.SM-001: false` means the rule **did not run**. It does not
  appear in `rules`/`rulesEvaluated` or in the text rule table.
- All rules disabled is an invalid configuration (exit 2).
- Each exclusion requires both an exact `rule` and an exact TOM `object`.
  The comparison is ordinal, case-sensitive. This alpha version does not
  support globbing or regex, preventing accidental broad suppressions.
- The rule and object must match a real finding; unmatched exclusions do not
  invent findings. Duplicate exclusions and exclusions for disabled/unknown
  rules are invalid.
- The engine first executes the enabled rules and validates raw SM-002
  consistency. Only then are specified findings suppressed before gate policy
  and rendering. This means a suppressed warning cannot fail `--fail-on-warning`.
- The JSON `summary.suppressed` field and optional text line report how many
  findings were suppressed. The JSON schema remains v1.0.0 in alpha with an
  additive documented property. SM-002 coverage still measures all applicable
  model measures, including explicitly excluded findings; it is not DAX
  execution or a claim of correctness.
- If SM-002 is disabled, `minMeasureCoverage` is not allowed because coverage
  would be unavailable. This combination fails closed (exit 2).

## Invalid configuration

Unsupported YAML keys, rules or version, malformed YAML, wrong types,
noninteger/out-of-range coverage, duplicates and unreadable config files fail
with `INVALID CONFIGURATION` (exit 2). JSON mode returns one JSON document on
stdout for application-level failures. Parser errors before `audit` action
still use standard System.CommandLine text diagnostics.

Privacy: error reports do not include raw YAML, DAX or absolute configuration
paths. A limited 64 KiB config file and 500-entry exclusion list keep the
parser bounded. This is a read-only local feature; symlink/junction containment
is not hardened against malicious repositories.

## Verify

```powershell
dotnet restore
dotnet build -c Release -warnaserror
dotnet test -c Release
dotnet format whitespace --verify-no-changes
.\scripts\test-audit-json.ps1
python scripts/verify-03-1-contract.py
git diff --check
```

Manual CLI smoke (no project config written):

```powershell
dotnet run -c Release --no-build --project src/PowerBIQualityGate.Cli -- audit examples/valid-pbir-tmdl --config examples/pbiq.example.yml --format json
```

## Scope deferred

- Rule-specific per-model configuration inheritance, complex glob patterns,
  per-file config, baseline severity overrides or dynamic rule plugins.
- SARIF suppressions, GitHub annotations, reusable GitHub Action (03.4/03.5).
- Fabric repositories without `.pbip` (03.6).
