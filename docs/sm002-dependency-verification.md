# SM-002.5c.1: Dependency verification and review evidence

## Intent

The opt-in `inspect --measures --dependencies` command reports **candidates**, not
confirmed DAX errors. In the first private PBIP pilot, SM-002.5b reduced the
candidate references from 29 to 11, leaving 11 references across 7 measures in
anonymous case P01 and 0 in P02. These totals do not tell us if the remaining
references are truly invalid.

This milestone deliberately **does not add a guessed confidence percentage** or
promote candidates to the SM-002 gate. It provides a safe workflow to compare
candidate sets and record **independent local review evidence** before any
future confidence model is designed.

## Files

- `scripts/sm002-dependency-review.py`: standard-library-only CLI for aggregate
  summaries and anonymous baseline comparisons.
- `tests/pilot/test_sm002_dependency_review.py`: synthetic-only regression suite.
- `docs/sm002-dependency-verification.md`: this guide.

**No modification** to DAX inference, SM-002 warnings, quality-gate thresholds,
exit codes, JSON v1, SARIF, the released binary, or the private model itself.

## Review taxonomy (fill ONLY in your private CSV)

Existing `--dependency-review` CSV files contain these **exact** columns:

`case_id,measure_table,measure_name,reference_table,reference_name,candidate_kind,review_status,notes`

Supported `candidate_kind` values (unchanged from 5a):

- `unknown_table`
- `unknown_qualified_member`
- `unresolved_unqualified`

Fill **only** the `review_status` field. Allowed values:

| Value | Meaning | Evidence standard |
| --- | --- | --- |
| *(blank)* | Not reviewed | No claim |
| `confirmed_missing` | Independent inspection/engine validation indicates a missing reference | Do not conclude from PBIQ's warning alone |
| `confirmed_valid` | Independent inspection/engine validation establishes a valid reference, e.g. a scoped virtual column | Do not conclude from name similarity alone |
| `uncertain` | Review performed, but DAX semantics or execution context prevent a reliable conclusion | Keep separate from matched/invalid counts |

`notes` is **PRIVATE**. You can record a short manual explanation, but do not
paste private notes, names, formulas, model paths or raw Power BI errors into
public commits or PRs.

A failed DAX query can be caused by a downstream issue and is not, by itself,
proof that PBIQ identified the correct missing reference. For unqualified
`[Name]` references in particular, review scope and context before assigning a
conclusive label. Different snapshots of a model must not be compared as if
only the analyzer had changed.

## Paths and privacy

Use a private folder **outside any Git worktree**. The script rejects inputs
and outputs located inside Git, symlinks/junctions, unexpected CSV columns,
unsupported review values, duplicate candidates, mixed case IDs, and attempts
to overwrite existing JSON. It prints only P0x aliases and aggregate counts.

**Never pass a private CSV through GitHub, an issue, or chat.** JSON has no
raw DAX or object identifiers, but inspect and approve aggregate counts before
publishing them because small groups can still be identifying.

## Compare SM-002.5a with SM-002.5b (local only)

```powershell
Set-Location "$HOME\Desktop\Porfolio\powerbi-quality-gate"
$private = Join-Path $HOME 'PBIQ Pilot Private'
foreach ($case in @('P01', 'P02')) {
    $before = Join-Path $private "sm0025a-${case}-dependencies-v2.csv"
    $after = Join-Path $private "sm0025b-${case}-dependencies.csv"
    $out = Join-Path $private "sm0025c-${case}-comparison.json"
    python .\scripts\sm002-dependency-review.py compare `
        --before $before --after $after --case-id $case --out $out
    if ($LASTEXITCODE -ne 0) { throw "Comparison failed for $case" }
}
```

Comparison uses case-insensitive identifiers only **inside the process** and
reports only totals: baseline, current, unchanged, no-longer-flagged, newly
flagged. In particular, case P02's latest CSV is allowed to have zero rows.
These counts do not prove that suppressed references were valid; they describe
only differences in *candidates*.

## Independently review the 11 remaining candidates in P01

Open your own local file
`$HOME\PBIQ Pilot Private\sm0025b-P01-dependencies.csv`.
Review each candidate in an authorized model in Power BI Desktop, checking the
appropriate source measure, dependent objects and scope. Do not change the
reference/measure columns or row identities. Record your status in
`review_status`, and save the file privately.

To preserve your original pilot output, it is best to make an editable copy
named `sm0025c-P01-reviewed.csv` outside Git. **The script never edits CSVs.**

```powershell
python .\scripts\sm002-dependency-review.py summarize `
    --review (Join-Path $private 'sm0025c-P01-reviewed.csv') `
    --case-id P01 `
    --out (Join-Path $private 'sm0025c-P01-summary.json')
if ($LASTEXITCODE -ne 0) { throw 'Private review summarization failed' }
```

For P02's zero candidates, a reviewer can summarize the header-only CSV by
passing `--case-id P02`. If no `review_status` is filled, the tool counts rows
as `unreviewed`, not confirmed errors or valid references.

## Validation

```powershell
dotnet test -c Release
dotnet build -c Release --no-incremental -warnaserror
python -m unittest discover -s tests/pilot -p 'test_*.py' -v
git diff --check
```

Acceptance:

1. Comparison and summary output reveal no model/table/measure/reference names.
2. Review statuses are entered only after independent validation; never
   auto-filled based on existing lexical candidate kinds.
3. P02 zero-candidate case is supported.
4. No change to the existing warning gate, scanner, pilot counts or TOM model.
5. Future 5c steps should follow the review evidence rather than adding
   indiscriminate heuristics to suppress candidates.
