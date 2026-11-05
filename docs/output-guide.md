# Reading PBIQ output

PBIQ provides different presentations for different jobs. `scan` is a human-readable structural report; `inspect --measures` is a compact metadata inventory; `audit` is a semantic-model quality gate. **The printed status, rule evidence, and process exit code should be read together.** Terminal colors and decorative glyphs are not an API.

## Three views, different questions

| View | Question answered | Caveat |
| --- | --- | --- |
| `scan` | Are the implemented PBIP structural checks satisfied? | Not complete Microsoft JSON Schema or Power BI Desktop validation |
| `inspect --measures` | How many visible/hidden measures and format/type metadata categories are present? | Inventory is **not** a determination of correct numeric formatting |
| `audit` | What did four metadata rules find and does the chosen gate allow it? | Audit can pass while measure-type coverage remains incomplete |

## Example: one-line audit

The following is a real, repeatable result from the **public synthetic** `examples/valid-pbir-tmdl` fixture (not a user/customer project):

```text
PBIQ | AUDIT: PASS WITH COVERAGE GAPS | 0 errors, 0 warnings, 3 information | exit code: 0
```

- **PASS WITH COVERAGE GAPS**: no blocking findings under the *current policy*, but some SM-002 candidates could not be evaluated as known numeric or known nonnumeric types using offline TOM metadata.
- **0 warnings**: no confirmed warnings were emitted by these rules. It does **not** mean every unclassified measure has a correct format.
- **3 information**: nonblocking review findings (in this fixture, visible technical/key columns).
- **Exit `0`**: a quality gate success under the selected policy, not full model certification.

Use `pbiq audit <path> --details` to inspect individual findings for **your own** model; use `--quiet` for scripts.

## Understanding all gate outcomes

| Status / message | Interpretation | Typical exit |
| --- | --- | :--: |
| `PASS` | Rules evaluated and no blocking finding or noted coverage gap | `0` |
| `PASS WITH ADVISORIES` | Only advisory/nonblocking findings under current policy | `0` |
| `PASS WITH COVERAGE GAPS` | Rules ran, but some measure types could not be classified offline; no blocking policy | `0` |
| `FAIL` | An Error finding blocked the gate | `1` |
| `FAIL (WARNING POLICY)` | Warning findings exist and `--fail-on-warning` is enabled | `1` |
| `FAIL (COVERAGE POLICY)` | SM-002 type-classification rate is below the explicitly configured minimum | `1` |
| `STRUCTURE VALIDATION FAILED` / audit not run | Input structure did not pass prerequisite checks | `1` |
| `PROJECT DISCOVERY FAILED` / `LOCAL TMDL REQUIRED` / TOM error | Audit could not be evaluated | `2` |
| `INVALID OUTPUT MODE` / `INVALID COVERAGE THRESHOLD` | Unsupported flag combination or threshold | `2` |
| `MEASURE QUALITY INTEGRITY FAILED` | An internal SM-002 aggregate consistency check failed | `2` |

When failures overlap, the active warning policy may take precedence over the coverage policy in the displayed verdict. **Do not infer additional reasons from the label alone; consult the detailed report and active options.**

## The difference between inventory coverage and SM-002 coverage

In `inspect --measures`:

- `Measures inspected: X/Y` means PBIQ enumerated measure metadata.
- `Format (visible): static / dynamic / missing` are mutually exclusive categories for **visible** measures. A dynamic definition takes precedence over a static fallback.
- `Type (visible): numeric / nonnumeric / unknown / unsupported` is a separate classification of TOM data-type metadata.
- Hidden measures are included in the overall inventory but not in visible-format/type partitions.

For a synthetic two-measure fixture, the output can be:

```text
Measures inspected: 2/2  (visible 2, hidden 0)
Format (visible): static 0  |  dynamic 0  |  missing 2
Type (visible): numeric 0  |  nonnumeric 0  |  unknown 2  |  unsupported 0
```

This is **100% inspection of available measure metadata**, but **0% numeric-type classification for SM-002 candidates**. These are different percentages. Missing format metadata for an `Unknown` type is a **review item**, not a confirmed missing numeric format.

For SM-002 coverage:

```text
checked    = visible measures with a known numeric TOM type
unresolved = visible candidate measures with unknown or unsupported type
candidates = checked + unresolved
coverage   = 100 * checked / candidates
```

Known nonnumeric and hidden measures are excluded from the candidate denominator. If `candidates == 0`, coverage is **N/A** and the minimum coverage policy does not fail for that reason. For a known numeric measure, a missing format can produce an SM-002 warning even when its **type-classification coverage is complete**.

The `audit` default summary includes a small **MEASURE QUALITY** section with format counts, confirmed SM-002 warnings and aggregate review counts. The `--details` breakdown categorizes additional review items but deliberately does not list hundreds of unclassified measures. A present format does not prove its semantic appropriateness.

## Levels and rule identifiers

- **Error**: a blocking model-quality finding when present.
- **Warning**: advisory by default, blocking with `--fail-on-warning`.
- **Information**: nonblocking suggestion; not an error.

Current shipped rules:

- **SM-001:** active bidirectional relationships. Bidirectional filtering may be intentional.
- **SM-002:** visible known-numeric measures without usable format metadata. Unknown types are not guessed from DAX.
- **SM-003:** visible technical/key-like columns. Key visibility can be intentional.
- **SM-004:** inactive relationships. Inactive relationships may be needed for alternate date roles.

Structural diagnostics are in the `PBI-001`–`PBI-018` range. See [current structural checks](current-checks.md) and [semantic-model rule pack](first-rule-pack.md) for exact triggers.

## Readability and color

- **Default `scan`:** branded ASCII PBIQ banner, project profile, checkpoints, diagnostic report and final status.
- **Default `audit`:** concise finding totals by rule, coverage and verdict. The large scan banner is intentionally absent.
- **`audit --details`:** grouped findings, affected locations and one shared WHY/HOW per identical guidance group. May expose customer/object names.
- **`audit --quiet`:** one plain-text line without ANSI, project path or object names; designed for CI.
- **`--no-color`:** turns off ANSI style; does **not** hide project paths or objects and does not change policy outcomes.
- **`NO_COLOR`:** environment flag respected by the CLI; redirected output suppresses color automatically.

Some terminal fonts render optional status glyphs as `?`. Prefer the explicit textual verdict and process exit code. This cosmetic issue is tracked for polish before the Release Candidate.

## Verify a failing policy locally

From a built source checkout in PowerShell:

```powershell
$cli = './src/PowerBIQualityGate.Cli/bin/Release/net10.0/pbiq.dll'

dotnet $cli audit './examples/warning-pbir-tmdl' --fail-on-warning --no-color
$LASTEXITCODE  # expected 1: intentional SM-001 warning

dotnet $cli audit './examples/valid-pbir-tmdl' --min-measure-coverage 95 --quiet
$LASTEXITCODE  # expected 1: unknown TOM measure types
```

## Sharing reports safely

PBIQ reads locally, but **the output itself may be sensitive**. `scan` can print paths; `inspect`, `inspect --tom` and `audit --details` can expose model metadata; `inspect --measures` prints a project filename and aggregate counts. Even aggregates may reveal business scale. Use the synthetic examples for README screenshots, GIF demos, bug reports and public docs. Review screenshots, terminal prompts and captures before committing.

**A PASS never certifies DAX correctness, performance, refresh success, relationship modeling quality or Power BI Desktop compatibility.**
