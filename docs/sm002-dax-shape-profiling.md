# SM-002.3a: privacy-safe DAX root-shape profiling

## Motivation

The local pilot found that the initial advisory-only lexical analyzer resolves
4 of 120 measures in representative P01 and 8 of 763 in representative P02.
Among these, 3 and 2 respectively are likely numeric without model-level format
metadata. Those are **review hints**, not confirmed SM-002 warnings.

Blindly growing the function allowlist would increase risk and could miss the
structures that account for the 871 unresolved expressions. The next step is to
count broad syntactic entry shapes without sending source DAX or object names
outside the user's machine.

## Behavior

For each visible measure with unknown/unsupported TOM type and **Unresolved**
static evidence, PBIQ records one coarse category based on the first lexical
token: `VAR`, `CALCULATE`, `DIVIDE`, conditional, iterator, other function,
reference/operator, other/comment-first. The profiler is **not a parser**:
these are starting-token buckets, not verified complete top-level syntax and
not result-type evidence. For example, `SUM(...) + 1` begins with a function
but is not necessarily a standalone function call.

`pbiq inspect <pbip> --measures` prints only fixed labels, the number of
unresolved expressions in each bucket, and the number in that bucket missing
model-level format metadata. It does not print expression snippets, arbitrary
DAX function names, measure or table names. Like the existing inspect mode it
still prints the project file basename, so **share only filtered aggregate
lines**, not the full private output.

No change to SM-002 warnings, quality coverage, gate thresholds, JSON v1, SARIF,
or the immutable `v0.3.0-alpha.1` release.

## Validation

```powershell
# New tests + original lexical regression tests
dotnet test -c Release --filter 'FullyQualifiedName~DaxRootShapeProfilerTests|FullyQualifiedName~StaticDaxTypeEvidenceTests'
dotnet test -c Release
dotnet build -c Release -warnaserror
python -m unittest discover -s tests/pilot -p 'test_*.py' -v
```

## Interpretation

- Prioritize **biggest unresolved + missing-format buckets** for safer parsing.
- Never convert a root bucket into a numeric SM-002 warning.
- Reconcile sum of bucket counts with `StaticDaxUnresolved` for each model.
- Review only fixed aggregate labels/counts when sharing results.
- Keep private model files, path manifests, detailed DAX and review CSV outside Git.
