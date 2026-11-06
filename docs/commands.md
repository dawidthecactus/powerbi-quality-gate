# PBIQ CLI reference

PBIQ is a read-only, offline CLI with three commands: `scan`, `inspect` and
`audit`. The v0.3 development source supports PBIP and Fabric Git inputs for
**`audit` only**. Public `v0.2.0-rc.1` executables do not contain the v0.3
options. Use `pbiq <command> --help` for the installed revision's own syntax.

## Inputs and command coverage

| Command | Accepted input | Purpose |
| --- | --- | --- |
| `scan <path>` | `.pbip` or directory with one `.pbip` | Implemented PBIP/PBIR structure checks (`PBI-001`–`PBI-018`), not a complete schema validator |
| `inspect <path>` | `.pbip` or its single-PBIP folder; local TMDL | Inventory of declarations and TOM metadata |
| `inspect <path> --tom` | PBIP + local TMDL | More detailed TOM view; may expose object metadata |
| `inspect <path> --measures` | PBIP + local TMDL | Aggregate format/type inventory without DAX or measure names |
| `audit <path>` | PBIP, native `.SemanticModel` folder or parent with exactly one model | Four SM metadata rules and policy gate |

A Fabric input is **not** a Fabric service URL: it is a local Git checkout.
A directory with multiple models must be narrowed to a single item; discovery
never chooses one arbitrarily. `scan` and `inspect` do not accept a standalone
Fabric Git item in this revision.

## Examples for source builds

```powershell
$cli = './src/PowerBIQualityGate.Cli/bin/Release/net10.0/pbiq.dll'

dotnet $cli scan examples/valid-pbir-tmdl
dotnet $cli inspect examples/valid-pbir-tmdl --measures
dotnet $cli audit examples/warning-pbir-tmdl --fail-on-warning --details

dotnet $cli audit examples/fabric-git/Revenue.SemanticModel --format json
dotnet $cli audit examples/fabric-git/Revenue.SemanticModel `
    --format sarif --sarif-source-root .
dotnet $cli audit examples/valid-pbir-tmdl `
    --config examples/pbiq.example.yml --format json
```

## Audit options

| Option | Effect |
| --- | --- |
| `--format text\|json\|sarif` | `text` default; JSON schema `1.0.0`; SARIF `2.1.0` |
| `--sarif-source-root <directory>` | Existing ancestor of project/model folder; **SARIF only**; use Git checkout root for GitHub Code Scanning |
| `--config <file.yml>` | Explicit YAML configuration; otherwise auto-discover `.pbiq.yml` beside PBIP or beside Fabric item |
| `--fail-on-warning` | Reject remaining warnings with exit `1` |
| `--min-measure-coverage <0..100>` | Require SM-002 classification coverage; if insufficient, exit `1` |
| `--details` | Group findings with affected objects and WHY/HOW advice (text only) |
| `--quiet` | One-line text verdict for scripts (text only) |
| `--no-color` | Disable ANSI; machine formats are ANSI-free already |

`--details` and `--quiet` are mutually exclusive, as are either of them and a
machine format. A custom SARIF source root is invalid without
`--format sarif`. `inspect --tom` and `inspect --measures` cannot be combined.

### Configuration file (`.pbiq.yml`)

```yaml
version: 1
rules:
  SM-001: true
  SM-002: true
  SM-003: true
  SM-004: false
policy:
  failOnWarning: true
  minMeasureCoverage: 95
exclusions:
  - rule: SM-003
    object: Sales[ProductId]
```

Unknown keys/rules, invalid values, duplicate exclusions and missing explicit
config files fail closed with exit `2`. Exclusions are exact, case-sensitive
rule/object pairs, not globs. At least one SM rule must remain enabled.
Coverage requirements need `SM-002` enabled. An explicit CLI coverage option
overrides YAML; CLI `--fail-on-warning` enables the strict policy even if YAML
says `false`. `summary.suppressed` (JSON) counts excluded findings.

## Rules and limitations

| Rule | Description | Default severity |
| --- | --- | --- |
| `SM-001` | Active bidirectional relationship | Warning |
| `SM-002` | Unformatted, visible **confirmed numeric** measure | Warning |
| `SM-003` | Visible technical/key column | Information |
| `SM-004` | Inactive relationship | Information |

An unresolved TOM measure type is **not** treated as numeric. Classification
coverage and review counts are distinct from format metadata presence. PBIQ
does not execute DAX or infer data types from expressions.

## Formats and streams

- **Text** summary/details/quiet: a successful audit writes to stdout;
  rejected/failed audits typically write to stderr.
- **JSON**: one JSON schema-v1 object on stdout after the audit handler runs,
  including exits `1` and `2`. `diagnostics[].location` can include verified,
  1-based TMDL file/line coordinates. It does not expose DAX expressions.
- **SARIF**: one SARIF 2.1.0 document on stdout after the audit handler runs.
  When a unique declaration cannot be mapped, the result has no physical
  location; no coordinates are fabricated. `--sarif-source-root` lets GitHub
  map TMDL files below a nested workspace to checkout-relative URIs.
- **Parser errors** before entering the handler (e.g. missing path/unknown
  flag) may still be plain text, even when a machine output was requested.

Do not infer success from an empty SARIF `results` array. Audit failures can
produce valid empty SARIF with unsuccessful invocation metadata. Always check
the process exit code and, for machine formats, the embedded outcome.

| Exit | Meaning for `audit` |
| --- | --- |
| `0` | Evaluated and accepted by configured quality policy, possibly with coverage gaps |
| `1` | Gate or prerequisite structural validation failed |
| `2` | Not evaluated: invalid input/format/config, TOM/rule failure, or incompatible options |

CLI output can contain confidential model names/object paths. Review before
sharing publicly. See [the JSON contract](audit-json-contract-v1.md),
[SARIF guidance](milestone-03.4-sarif-github-annotations.md) and
[CI integration](ci-integration.md).
