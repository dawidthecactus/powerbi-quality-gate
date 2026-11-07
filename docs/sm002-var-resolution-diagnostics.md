# SM-002.3c: privacy-safe VAR/RETURN resolution diagnostics

## Why this milestone exists

SM-002.3b's bounded `VAR ... RETURN` lexical analyzer passed all 529 .NET tests, but the local real-model pilot found **0 of 572** VAR-root expressions resolved: P01 0/69 and P02 0/503. These were only two representative model inputs; other pilot representations may duplicate them. The observation is a **negative coverage result**, not evidence of invalid DAX or any formatting defect.

This milestone does not broaden type inference. It explains where the narrow analyzer stops, using only fixed, anonymous aggregate categories.

## Diagnostic model

`DaxVarReturnEvidence.AnalyzeDetailed` reports an evidence class and, **only if unresolved**, a bounded reason:

- `StructureRejected`: a declaration/RETURN layout, delimiter, quoted identifier, unsupported variable spelling, or other **analyzer-specific** structural condition was rejected. Does **not** assert that Power BI would reject the expression.
- `UnsupportedReturnExpression`: the expression after RETURN was not one of the narrow scalar shapes or a bare supported variable alias.
- `UnsupportedVariableExpression`: RETURN resolved to an earlier variable but the variable's value expression was outside the narrow scalar grammar.
- `UnknownOrForwardVariable`: a bare alias was not defined in an allowed earlier declaration (including forward references).
- `AnalysisLimitExceeded`: length, variable count or alias-depth bound was reached.

`BlockedRootShape` is recorded only for unsupported return or variable expressions, with **fixed shape labels** from the existing `DaxRootShapeProfiler`: `DIVIDE`, `CALCULATE`, conditionals, iterators, references/operators, other functions, and the other predefined buckets. Shape counts are a **subset** of unresolved reasons, not a second set of measures.

The original `DaxVarReturnEvidence.Analyze` API now calls `AnalyzeDetailed` and returns just `.Evidence`, preserving the existing behavior. Valid numeric or nonnumeric evidence has reason `NotApplicable`. Diagnostics never trigger warnings.

## Privacy, scope, and compatibility

Only **visible** measures that were previously categorized as unresolved VAR-root shapes enter these counters. The CLI adds two optional sections to `pbiq inspect <PBIP> --measures`:

```text
VAR / RETURN unresolved reasons (count / missing format; approximate, advisory only):
  Unsupported variable expression: N / N
  Unsupported RETURN expression: N / N
  Structure / tokenizer rejected: N / N
VAR / RETURN blocked expression shapes (subset; count / missing format):
  CALCULATE / CALCULATETABLE: N / N
  DIVIDE: N / N
```

The actual order and visible categories depend on which buckets contain nonzero counts. There are **no** names, model paths, DAX snippets, variable identifiers, raw diagnostics or free-form error messages in these new sections. The existing inspect header **does print the PBIP file basename**, so only share the filtered aggregate lines from a private model.

Unchanged: confirmed SM-002 warnings, `--min-measure-coverage`, default audit verdict, exit codes, JSON v1, SARIF, the original static evidence counters, existing root-shape counters and immutable tag `v0.3.0-alpha.1`. No source TMDL mutation or DAX evaluation is performed.

## Validation gate

```powershell
dotnet test -c Release --filter 'FullyQualifiedName~DaxVarReturnDiagnosticsTests|FullyQualifiedName~DaxVarReturnEvidenceTests'
dotnet test -c Release
dotnet build -c Release -warnaserror
python -m unittest discover -s tests/pilot -p 'test_*.py' -v
git diff --check
```

For each representative local model, **the sum of all unresolved-reason counts must equal** `VarReturnUnresolved`: 69 for P01, 503 for P02 based on the previous baseline. The blocked-root-shape sum cannot exceed that. Compare counts only and do not commit or upload manifest CSV, private model DAX, names or detailed reviewer evidence.

## Next decision

If `StructureRejected` dominates, focus on a genuine DAX tokenization / grammar layer and evaluate parser options. If `UnsupportedVariableExpression` dominates, inspect only fixed blocked-root-shape counts to prioritize safe semantic wrappers, e.g. `CALCULATE` or `DIVIDE`. If `UnsupportedReturnExpression` dominates, prioritize RETURN forms. **Do not** infer numeric type for an unsupported expression merely because the top-level function name looks numeric.
