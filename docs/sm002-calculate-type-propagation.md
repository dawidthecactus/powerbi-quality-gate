# SM-002.3d: advisory scalar evidence through CALCULATE

## Evidence for prioritization

The P01/P02 privacy-safe diagnostic pilot observed 562 unresolved `VAR` expressions
with unsupported RETURN expressions. A subset of **187** was blocked by a
root-shaped `CALCULATE / CALCULATETABLE` expression (P01 37, P02 150). The bucket
does **not** distinguish those functions or imply that all 187 are classifiable.
The pilot is limited to two representative inputs, which may share model logic.

Microsoft documents that `CALCULATE(<expression>, filters...)` evaluates a
**scalar expression** in a changed filter context, while `CALCULATETABLE`
evaluates a **table expression**. `CALCULATETABLE` must never be treated as
numeric scalar evidence simply because it shares the `CALCULATE` prefix.

## Implementation

`DaxCalculateScalarArgument.TryExtract` is a bounded, lexical **internal**
helper. It accepts only a complete top-level `CALCULATE(...)` call, extracts the
first argument, and checks balanced delimiters and nonempty argument segments.
Commas or semicolons inside nested parentheses, braces, quoted table names,
bracket identifiers or strings do not split arguments. Explicit comments within
the helper are rejected; comments masked by the outer `VAR` scanner appear as
whitespace. Limits: 16,384 characters, 64 nested delimiters, 64 arguments.

`DaxVarReturnEvidence.Resolve` applies the helper to an existing VAR definition
or a complete `RETURN CALCULATE(...)` expression. When a wrapper is recognized,
the analyzer recursively classifies the first argument with the existing
`StaticDaxTypeEvidence` logic and previously declared aliases. At most 32 alias
or wrapper edges are traversed.

```dax
VAR m = CALCULATE(SUM('Sales'[Amount]), 'Date'[Year] = 2026)
RETURN m
```

This yields advisory `LikelyNumeric` because the first argument is a known
lexical numeric form. `VAR m = CALCULATE("label") RETURN m` is
`LikelyNonNumeric`. Invalid or unknown first arguments such as `[Measure]`,
`DIVIDE(...)`, `IF(...)` and `CALCULATETABLE(...)` stay unresolved. The helper
**does not type-check filter expressions** or validate entire DAX semantics.
A lexical type hint is not proof that a measure is valid or correctly formatted.

When propagation fails, the existing unresolved reason and blocked-root
category are preserved. On success, the separate `VAR / RETURN evidence`
aggregates change. The original `Static DAX evidence` and DAX root-shape counts
remain unchanged.

## Compatibility and privacy invariants

- **Never promotes inferred types to SM-002 warnings.** The TOM-confirmed
  numeric classification, coverage thresholds and CI gate verdict are unchanged.
- CLI JSON v1, SARIF, exit codes and published `v0.3.0-alpha.1` are unchanged.
- No DAX execution, source TMDL mutations, or network transfer.
- No DAX/measure/table identifiers in the additional aggregates. The existing
  `inspect` header can include the PBIP basename, so only share filtered
  aggregate lines from private models.
- Deliberately **does not** classify standalone, non-VAR CALCULATE measures.
  This patch affects only previously unresolved VAR candidates.

## Validation

```powershell
dotnet test -c Release --filter 'FullyQualifiedName~DaxCalculateScalarEvidenceTests|FullyQualifiedName~DaxVarReturnDiagnosticsTests|FullyQualifiedName~DaxVarReturnEvidenceTests'
dotnet test -c Release
dotnet build -c Release -warnaserror
python -m unittest discover -s tests/pilot -p 'test_*.py' -v
git diff --check
```

Then compare P01/P02 aggregate `VAR / RETURN evidence`, missing-format review
counts and unresolved reasons against the baseline (69 and 503 unresolved,
respectively). Never commit/upload the manifest, private models, original DAX,
object names, paths or per-measure review notes.

## References

- https://learn.microsoft.com/en-us/dax/calculate-function-dax/
- https://learn.microsoft.com/en-us/dax/calculatetable-function-dax/
