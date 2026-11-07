# SM-002.2: advisory static DAX type evidence

## Why

The offline TOM serializer returns `Unknown` for result types of local TMDL measures, including simple numeric `SUM` and `DISTINCTCOUNT` measures (see SM-002.1 integration tests). In a privacy-safe pilot, representative models had 120/120 and 763/763 unknown measure types. That meant **zero strict SM-002 classification coverage**, even though metadata showed 67 and 659 missing formats.

A missing format is a **fact about metadata**, not proof of a numeric-formatting defect. Static DAX analysis must never silently turn it into a confirmed Warning.

## This milestone's contract

`StaticDaxTypeEvidence` is a deliberately narrow lexical classifier. It does not run DAX or validate expression semantics. It is independent of TOM data-type classification:

- **LikelyNumeric:** a complete top-level one-argument `COUNTROWS`, `COUNT`, `COUNTBLANK`, `DISTINCTCOUNT`, `DISTINCTCOUNTNOBLANK`, `SUM` or `AVERAGE` call, or a standalone invariant numeric literal.
- **LikelyNonNumeric:** a complete standalone text literal or a zero-argument `TRUE()` / `FALSE()` call.
- **Unresolved:** everything else, including `VAR`, `CALCULATE`, `IF`, `SWITCH`, `FORMAT`, `MIN`, `MAX`, operators, comments, invalid/incomplete expressions and oversized expressions.
- **NotAssessed:** the offline TOM type was already known, so the auxiliary heuristic is unnecessary.

For calls, a structural scan checks balanced parentheses and DAX-quoted strings/identifiers; unknown or malformed syntax remains unresolved. Because this is **lexical evidence only**, a function's arguments are **not semantically checked**. An invalid expression can look like a supported function. Treat all suggestions as advisory and manually verify them in Power BI Desktop or with a trusted model engine.

Only measures with unknown/unsupported TOM type are heuristically assessed. The CLI reports **aggregate counts**, never DAX, measure names or format patterns:

```text
Type (visible): numeric 0  |  nonnumeric 0  |  unknown 2  |  unsupported 0
Static DAX evidence (TOM unknown/unsupported): likely numeric 2  |  likely nonnumeric 0  |  unresolved 0
Static DAX review: missing format among likely numeric 2 (advisory only; not SM-002 warnings)
```

Both `inspect <PBIP> --measures` and `audit <PBIP-or-Fabric> --details` expose these aggregate hints; the default `audit` report already exposes format counts and review items.

## Compatibility and safety invariants

- `SM-002` only emits a Warning for a **TOM-confirmed numeric** visible measure with missing formatting. No inferred type is promoted to confirmed.
- `--min-measure-coverage` uses the original TOM type-classification rate, not inferred hints. Quality gate status and exit codes are unchanged.
- JSON v1 / SARIF output fields and semantics are unchanged in this patch.
- The stable release `v0.3.0-alpha.1` is immutable. This is development work for a future pre-release.
- Analysis is read-only/offline; it never mutates TMDL, invokes Fabric, or evaluates DAX.
- This classifier intentionally cannot infer types from arbitrary `CALCULATE`, `VAR`, `IF`, `SWITCH`, or complex expressions. Those require a more complete semantic analyzer and validation.

## Testing

```powershell
dotnet test -c Release --filter 'FullyQualifiedName~StaticDaxTypeEvidenceTests|FullyQualifiedName~Sm002OfflineTypeEvidenceTests'
dotnet test -c Release
dotnet build -c Release -warnaserror
python -m unittest discover -s tests/pilot -p 'test_*.py' -v
```

One public synthetic TOM integration test confirms the separate evidence for `SUM` and `DISTINCTCOUNT`. Negative cases cover quoting, comments, trailing expressions, malformed input, ambiguous functions and missing format handling. If any future tests reveal uncertain inference, narrow the allowlist rather than increase default warnings.

## Follow-up (SM-002.3)

Validate advisory type counts on representative private models, compare model-engine types locally, evaluate whether an explicit opt-in policy would be safe, and design versioned machine-report fields only after review. Do not enable warnings based on inferred types in SM-002.2.
