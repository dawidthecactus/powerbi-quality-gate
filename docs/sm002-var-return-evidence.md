# SM-002.3b: VAR / RETURN advisory type evidence

## Motivation and pilot baseline

Prior PBIQ profiling of two representative private model inputs found **572**
visible unresolved measures starting with `VAR` out of **871** unresolved
measures overall: P01 **69**, P02 **503**. Of those VAR-entry measures,
**509** had no model-level format metadata (P01 40; P02 469).

These are **lexical root-shape buckets**, not independently validated DAX
structures, type evidence or confirmed formatting defects. Other representations
from the private pilot may share the same underlying model logic.

## New behavior

`DaxVarReturnEvidence.Analyze` is a **bounded, advisory-only** reader for a
strict subset of measure expressions:

- Requires one or more top-level `VAR name = expression` declarations followed
  by exactly one top-level `RETURN expression`.
- Masks `//`, `--`, and `/* ... */` comments while keeping keyword positions
  stable; quoted strings, single-quoted table names, bracket references,
  parentheses and braces cannot inject fake top-level VAR/RETURN tokens.
- Rejects unbalanced delimiters and quotes, empty/duplicate declarations,
  unsupported variable spellings, forward aliases, missing return expressions,
  excessively long expressions and more than 64 variable definitions.
- For a standalone RETURN expression, reuses the previous narrow
  `StaticDaxTypeEvidence` numeric/text/boolean literal and allowlisted root
  scalar-function checks. For `RETURN variable`, follows only previously
  declared **bare identifier** aliases to a resolvable scalar expression.
  Stops at 32 alias links. No table-expression result type is inferred.
- Returns `LikelyNumeric`, `LikelyNonNumeric`, or `Unresolved`. All are
  **lexical hints**, not DAX semantic validation or Microsoft TOM types.
  An unsupported or syntactically plausible but semantically invalid expression
  may still yield a hint.

The new pass is called only for *visible* measure summary entries that already
have unknown or unsupported TOM type, remain unresolved in the existing
`StaticDaxTypeEvidence` classifier, and start with the `VAR` root-shape bucket.
It also exists as a standalone pure helper for tests. Hidden measurements do
not enter aggregate counts.

For auditing purposes the existing `Static DAX evidence` and `Unresolved DAX
entry shapes` counts are intentionally **unchanged**. A separate section in
`pbiq inspect <project> --measures` reports:

```text
VAR / RETURN evidence (unresolved TOM/DAX): likely numeric N  |  likely nonnumeric N  |  unresolved N
VAR / RETURN review: missing format among likely numeric N (advisory only; not SM-002 warnings)
```

These fixed labels and counters are the only new user-facing values. No DAX,
measure/table/variable names, format patterns or table model files are emitted.
**Caution:** `inspect` also prints the PBIP filename outside this new section;
share only manually filtered, anonymous aggregate lines from private models.

## Non-goals and compatibility

- No engine execution, no full DAX AST, no `CALCULATE`/`DIVIDE`/`IF` type
  inference, no expression correctness or format appropriateness certification.
- No changes to SM-002 confirmed warnings or the `--min-measure-coverage`
  denominator/threshold, policy/exit code, audit JSON v1, SARIF or the
  immutable `v0.3.0-alpha.1` release.
- No reading or transmitting user model files outside the existing read-only
  local inspection. The new code does not write TMDL.
- Future release milestones should validate lexical hints against a trusted
  DAX model engine and consider machine-readable extensions only with an
  explicit versioned contract change.

## Validation

```powershell
# 3b regression + existing 3a/2 tests

dotnet test -c Release --filter 'FullyQualifiedName~DaxVarReturnEvidenceTests|FullyQualifiedName~DaxRootShapeProfilerTests|FullyQualifiedName~StaticDaxTypeEvidenceTests'
dotnet test -c Release
dotnet build -c Release -warnaserror
python -m unittest discover -s tests/pilot -p 'test_*.py' -v
git diff --check
```

After local tests, run `inspect --measures` on only P01 and P02 using the
private manifest and share **only filtered, aggregate** output lines.

Reference: [Microsoft VAR keyword documentation](https://learn.microsoft.com/en-us/dax/var-dax).
