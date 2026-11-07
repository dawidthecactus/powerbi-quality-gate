# SM-002.3f: privacy-safe conditional resolution diagnostics

## Motivation

The previous representative private-model pilot (SM-002.3e) produced
**155/883** advisory static type hints across P01 and P02, but the VAR/RETURN
analyzer still stopped at **184 conditional-shaped expressions** (P01 **4**,
P02 **180**; P02 missing-format subset **171**). These are approximate lexical
root shapes, not independently verified DAX functions, proven numeric types or
formatting defects. The pilot has only two representative inputs.

The objective of this milestone is **diagnosis**, not further type inference.
It adds fixed-label aggregate breakdowns to explain *why* unresolved
conditional-root expressions were not recognized. Existing evidence classes,
quality findings and gate coverage must remain unchanged.

## New fixed diagnostic reasons

An unresolved expression whose final `VarReturnBlockedShape` is
`Conditional` receives exactly one additional reason:

- `UnsupportedCallShape`: a conditional starting token could not be extracted
  as one complete, supported `IF`, `SWITCH` or `COALESCE` call. The expression
  may still be valid DAX outside this narrow helper, e.g. unsupported
  `IFERROR`; this is **not** an invalid-DAX claim.
- `UnresolvedResultBranch`: the classifier stopped at a possible result
  expression whose type is unknown. Its **first** blocked branch gets an
  additional coarse lexical root-shape bucket (e.g. reference, DIVIDE,
  CALCULATE, iterator). This is not exhaustive for the entire DAX expression.
- `MixedResultTypes`: the classifier saw incompatible *known advisory* result
  categories (likely numeric vs likely nonnumeric), and returned Unresolved.
  There may also be unvisited branches after the mismatch.
- `OnlyBlankResults`: every explicitly provided result branch was `BLANK()`;
  the existing analyzer cannot assign a scalar type.

Reason order follows the pre-existing short-circuit logic: the classifier stops
at the first unrecognized or conflicting branch. An expression containing both
an unrecognized branch and mixed known types may therefore appear under
`UnresolvedResultBranch` rather than `MixedResultTypes`, depending on the order
of branches. **Do not interpret reason counts as semantic type frequencies.**

Expressions that stop on the existing analysis-step limit retain their
`AnalysisLimitExceeded` reason. They are not counted as blocked conditional
shapes and are outside this new breakdown.

## CLI output

`pbiq inspect <path> --measures` adds only fixed aggregate labels and numbers:

```text
VAR / RETURN conditional unresolved reasons (count / missing format; advisory only):
  Unsupported conditional call shape: N / N
  Unresolved result branch: N / N
  Mixed numeric / nonnumeric result kinds: N / N
  Only BLANK results: N / N
VAR / RETURN first unresolved conditional branch shapes (subset; count / missing format):
  CALCULATE / CALCULATETABLE: N / N
  Reference / operator / literal: N / N
```

Only nonzero buckets appear. All additional counts are derived from **visible
measures with Unknown/Unsupported TOM result types** that have unresolved
advisory VAR evidence and a final blocked shape of `Conditional`.

**Reconciliation invariants** for each local model:

1. Sum of conditional-reason counts = `IF / SWITCH / COALESCE` entry in
   `VAR / RETURN blocked expression shapes`.
2. Sum of first-unknown-branch shape counts = `Unresolved result branch`
   conditional-reason count.
3. All corresponding missing-format subtotals reconcile the same way.
4. The previous `VAR / RETURN evidence` numeric/nonnumeric/unresolved totals,
   original static type hints, root-shape counts, and SM-002 gate results
   should not change between 3e and 3f for identical models.

For the P01/P02 baseline, expect sums **4** and **180** for conditional reasons
respectively, unless the model inputs have changed. This does **not** predict
how those cases are partitioned across reasons.

## Privacy, compatibility, limitations

- No DAX, measure names, model paths, table/variable names, dynamic format
  expressions, pattern strings or snippets are emitted by the new sections.
- The existing inspect header can still print a PBIP basename. **Share only
  filtered anonymous aggregate lines** from P01/P02.
- No source TMDL changes, DAX evaluation or transmission of private data.
- Confirmed SM-002 warnings, `--min-measure-coverage`, exit codes, JSON v1,
  SARIF and the published immutable `v0.3.0-alpha.1` remain unchanged.
- This is lexical diagnostics, not proof of type soundness. In particular,
  `LikelyNumeric` and `LikelyNonNumeric` are **not** TOM-certified.

## Test plan

```powershell
dotnet test -c Release --filter 'FullyQualifiedName~DaxConditionalResolutionDiagnosticsTests|FullyQualifiedName~DaxConditionalScalarEvidenceTests|FullyQualifiedName~DaxVarReturnDiagnosticsTests'
dotnet test -c Release
dotnet build -c Release -warnaserror
python -m unittest discover -s tests/pilot -p 'test_*.py' -v
git diff --check
```

After tests, run the local P01/P02 aggregate-only pilot and compare the
reconciliation invariants above. Do not upload/commit manifest CSV, raw DAX,
private review CSV or per-measure data.

## Recommended next step: validate correctness, not just coverage

Before adding another function allowlist, independently validate a small,
locally selected sample of both numeric and nonnumeric hints against actual
Power BI Desktop / tabular-engine behavior. Record only anonymous aggregate
TP/FP/unknown outcomes and the selection method; retain DAX and identifying
measure names **locally**. The current CLI exposes only aggregates, not a
per-measure export, so independent sampling will need a separately designed
local-only workflow. Do not infer precision from green unit tests or pilot
coverage percentages.
