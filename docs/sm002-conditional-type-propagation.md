# SM-002.3e: conservative conditional result-type evidence (VAR / RETURN)

## Motivation

The anonymous P01/P02 pilot found **303** unresolved VAR-entry expressions
blocked at an `IF / SWITCH / COALESCE` starting shape (P01 8; P02 295).
The earlier CALCULATE step added 24 numeric hints in P01 but none in P02.
These counts reflect only two representative private model inputs and do
not prove broader coverage or type correctness.

## Supported syntax

`DaxConditionalScalarArguments` is an **internal bounded lexical splitter**, not
an AST/parser or semantic checker. It recognizes only complete calls to:

- `IF(test, true [, false])`: analyze the explicit result branches, considering
  an omitted false result to be implicit BLANK.
- `SWITCH(selector, value, result [, value, result]... [, else])`: analyze only
  each result position and optional else. Omitted else is implicit BLANK.
- `COALESCE(expression, expression [, expression]...)`: analyze every possible
  return expression, even if an earlier input is a literal. DAX permits mixed
  input types, so mixed evidence is **Unresolved**.

The helper checks balanced parentheses/braces, quoted strings and table names,
escaped bracket identifiers, argument separators, nonempty segments, a complete
root call, up to 64 arguments, 64 nested delimiters and 16,384 characters.
Outer VAR scanning has already masked comments. Unmasked comments, malformed
calls, unknown function variants (`IFERROR`, `IF.EAGER`) and trailing operators
are not recognized.

For each possible return expression, the existing `DaxVarReturnEvidence.Resolve`
recursively reuses **only** its pre-existing conservative scalar recognizers,
`CALCULATE` first-argument propagation and earlier `VAR` aliases. An explicit
`BLANK()` is treated as type-neutral; a call with only BLANK outcomes stays
Unresolved. The analyzer returns a hint only if **all non-BLANK branches**
resolve and agree on the existing coarse evidence category:
`LikelyNumeric` or `LikelyNonNumeric`. Any unknown branch or disagreement yields
`Unresolved`. A shared budget of 512 resolution steps and the existing depth
limit prevent pathological repeated expansion.

Examples:

```dax
VAR r = IF([Condition], SUM('Sales'[Amount]), 0)
RETURN r
```

Advisory `LikelyNumeric`.

```dax
VAR r = IF([Condition], 100, "N/A")
RETURN r
```

`Unresolved` because branches disagree.

```dax
VAR r = COALESCE([OtherMeasure], 0)
RETURN r
```

`Unresolved` because the other measure's type is unknown offline.

### Limitations

The helper **does not evaluate** branch tests, validate DAX predicates, check
all function argument semantics, distinguish boolean vs text within
`LikelyNonNumeric`, or confirm actual result types. The numeric/static hints
cannot be promoted to SM-002 warnings. A syntactically plausible but invalid
DAX expression can still yield a lexical hint.

IF can produce variant values when branches differ in type; SWITCH documents
same-type result expressions; COALESCE permits different result types.
Consequently, guessed numeric classification based on just one branch would
be incorrect.

## Compatibility and private data

- Only previously unresolved *visible* VAR-root measures are analyzed. Standalone
  non-VAR IF/SWITCH/COALESCE measures remain outside scope.
- The existing static-evidence, root-shape and VAR candidate counts retain their
  definitions. Only separate VAR advisory evidence and diagnostic aggregates
  can change.
- SM-002 confirmed warnings, coverage gate denominators/thresholds, JSON v1,
  SARIF, exit codes and the immutable alpha release remain unchanged.
- No model files, formulas, names or secrets are sent off-device or emitted in
  conditional diagnostics. The general inspect header still includes a PBIP
  basename: **share only filtered aggregate lines**, not raw CLI output.

## Validation

```powershell
# New and existing regression suites
dotnet test -c Release --filter 'FullyQualifiedName~DaxConditionalScalarEvidenceTests|FullyQualifiedName~DaxCalculateScalarEvidenceTests|FullyQualifiedName~DaxVarReturnDiagnosticsTests|FullyQualifiedName~DaxVarReturnEvidenceTests'
dotnet test -c Release
dotnet build -c Release -warnaserror
python -m unittest discover -s tests/pilot -p 'test_*.py' -v
git diff --check
```

Compare anonymous P01/P02 `VAR / RETURN evidence`, missing-format review
counts, `VAR / RETURN unresolved reasons`, and blocked shape totals with
SM-002.3d. **Do not commit private manifests, source DAX, measure names or
per-measure review notes.** Real-world success is not assumed from green tests.

## Documentation

- https://learn.microsoft.com/en-us/dax/if-function-dax
- https://learn.microsoft.com/en-us/dax/switch-function-dax
- https://learn.microsoft.com/en-us/dax/coalesce-function-dax
