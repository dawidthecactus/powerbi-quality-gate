# Milestone 02.4.4 — Minimal terminal experience

## Problem

A real Nielsen project exposed 14 informational SM-003 findings. The full
renderer repeated the same why/fix guidance for every column and produced a
wall of text. The default audit should answer **status**, **counts**, **rule**
and **coverage gap** without listing every model object.

## Output contract

| Command | What is printed | Colors | Exit codes |
| --- | --- | --- | --- |
| `pbiq audit <path>` | Compact project, verdict, counts, grouped rule totals and coverage | If terminal supports ANSI | Unchanged 0/1/2 |
| `pbiq audit <path> --details` | Same header, then every finding grouped by rule; shared WHY/HOW once per distinct guidance | If supported | Unchanged 0/1/2 |
| `pbiq audit <path> --quiet` | Exactly one line with verdict, aggregate counts, exit code | Never | Unchanged 0/1/2 |
| `pbiq audit <path> --no-color` | Plain-text variant of selected mode | Never | Unchanged 0/1/2 |

`--quiet` and `--details` cannot be combined. A conflicting request exits `2`
with `INVALID OUTPUT MODE`; no model is loaded. Existing `NO_COLOR` and
redirected-stream handling continues to apply.

For a model with unknown-formatting-type measures, the summary retains
`PASS WITH COVERAGE GAPS` and explicit `SM-002 coverage`, including the
unclassified count. Adding `--min-measure-coverage 95` continues to return
`FAIL (COVERAGE POLICY)` and exit code `1` whenever measured coverage falls
below 95%. The renderer does **not** change the quality rule pack, policy
outcome or findings.

```text
PBIQ  /  SEMANTIC MODEL AUDIT
────────────────────────────────────────────────────────
  NielsenTEST.pbip
  36 tables · 763 measures · 5 relationships

  ▍ AUDIT: PASS WITH COVERAGE GAPS  exit code: 0
    0 errors · 0 warnings · 14 information · 4 rules

  CHECKS
    ✓  SM-001  Relationship direction    0 findings
    !  SM-002  Measure formatting        0/763 checked
    i  SM-003  Technical columns         14 findings
    ✓  SM-004  Inactive relationships    0 findings

  SM-002 coverage: 0.0% (0/763 candidates) · 763 unclassified
  Coverage gap: formatting not checked for unclassified measure types
  Tip: audit <path> --details · inspect findings and guidance
```

The output above is illustrative, based on aggregate real-world counts.
The visual hierarchy is strongest in ANSI-capable terminals; `--no-color`
and redirected output preserve the same information in plain text. Avoid
publishing detailed model output for work/customer models without review.

## Design details

- Compact output keeps a restrained header rule, a color-coded verdict,
  aligned rule rows and one actionable coverage note. It avoids the large
  ASCII logo, boxed panels, object locations and repeated advisory paragraphs.
- Details print every affected location. The standard SM-003 key-column
  message is collapsed to the location alone, since its wording repeats the
  column name; custom SM-003 messages and other rules remain intact. WHY/HOW
  guidance is shared only when its *text* matches.
- The plain-text pathway bypasses Spectre markup. Colored output escapes
  literal bracket-like model metadata. Terminal control characters in
  metadata are normalized.
- Quiet output is always a single plain-text line. It intentionally omits
  project names, model metadata and affected objects.
- Failure summaries provide a reason and a short next action; detailed mode
  additionally displays the requested path and the original WHY/HOW.
- The previous full `ConsoleAuditRenderer` remains available to existing
  library callers/tests. CLI uses `CompactAuditRenderer` for all audit modes.

## Tests and CI

Regression tests check concise output size, deterministic finding groups,
shared guidance, literal markup, no ANSI in plain/quiet mode, failure paths,
three gate exit codes and stdout/stderr separation. CI smoke checks the
`--details` and `--quiet` modes on Windows and Linux alongside existing policy
checks.

```powershell
dotnet clean -c Release
dotnet restore
dotnet format whitespace
dotnet build -c Release -warnaserror
dotnet test -c Release --no-build
dotnet format whitespace --verify-no-changes
git diff --check
```

All model audits remain local, offline and read-only. A compact PASS does not
certify DAX or numerical correctness.
