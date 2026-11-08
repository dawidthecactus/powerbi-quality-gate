# SM-002.4: private evidence validation pilot

## Why

SM-002.3f reported **155 / 883** advisory type hints on two representative
private semantic models (P01 and P02), not confirmed TOM result types. Green
unit tests and high lexical coverage do **not** prove that these hints agree
with the type of a measure evaluated in Power BI. This milestone introduces an
independent, intentionally manual local review, not a new DAX heuristic.

No inferred evidence becomes an SM-002 warning. The strict quality gate,
coverage denominator, JSON v1 and SARIF remain unchanged.

## Privacy model (read before running)

`pbiq inspect <project> --measures --evidence-sample <private.csv> --case-id P01`
creates an **opt-in private CSV**, containing table and measure names for
reviewer lookup. **This CSV is identifying, not sanitized!** It must never be
committed, uploaded, pasted into a chat, or attached to a pull request.

- Both the sample and later summary destination must be **outside all Git
  worktrees**, including worktrees with `.git` files. Directories that are
  symlinks/junctions are rejected to avoid bypassing the Git guard.
- Each destination must be a new path in an existing directory. Existing files
  cannot be overwritten. Prefer approved, non-synced local storage.
- Export contains NO DAX, format strings, source path, connection properties,
  literal values from expressions or raw Power BI errors.
- The `--measures` terminal header still prints the project basename: **do not
  share raw `inspect` output**, only filtered anonymized aggregates.
- Output is deterministic: at most 12 items per stratum of `static` / `var` and
  `numeric` / `nonnumeric`. The stable SHA-256 ordering is a sampling mechanism,
  **not** anonymization or a guarantee of representativeness.
- Eligible measurements are **visible**, have `Unknown` / `Unsupported` TOM
  types, and have either static or VAR advisory hints. Both kinds are sampled
  without changes to underlying classification. Known TOM type, hidden, and
  unresolved measures are excluded.

## Create private sample files

From the source repository, with .NET 10 available:

```powershell
Set-Location "$HOME\Desktop\Porfolio\powerbi-quality-gate"
dotnet build -c Release -warnaserror

$private = Join-Path $HOME 'PBIQ Pilot Private'
$manifest = Import-Csv (Join-Path $private 'manifest.csv')
$cli = '.\src\PowerBIQualityGate.Cli\bin\Release\net10.0\pbiq.dll'

$cases = $manifest | Where-Object { $_.case_id -in @('P01', 'P02') }
foreach ($entry in $cases) {
    $privateCsv = Join-Path $private ("sm002-validation-{0}.csv" -f $entry.case_id)
    & dotnet $cli inspect $entry.path --measures `
      --evidence-sample $privateCsv --case-id $entry.case_id | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Private export failed: $($entry.case_id)" }
    Write-Host "Created private CSV for $($entry.case_id), outside Git"
}
```

Do **not** add CSV files to the repository. The sample columns are:

| Column | Handling |
| --- | --- |
| `case_id`, `sample_id` | Local aliases P01 / S001; not model-wide stable IDs |
| `table_name`, `measure_name` | **PRIVATE**, use only to locate measure in Power BI |
| `evidence_source` | `static` or `var` |
| `predicted_kind` | PBIQ advisory `numeric` or `nonnumeric` |
| `observed_kind` | **Reviewer fills**: `numeric`, `nonnumeric`, `variant`, or `unknown` |
| `notes` | Optional private reviewer details, **never shared** |

The raw sample contains no DAX, but **names are sensitive**. It escapes
spreadsheet-formula-like object names to reduce spreadsheet injection risks.
Do not remove or reorder rows. Do not change `predicted_kind`. Use an
independent observation, never copy PBIQ's prediction as the answer.

## How to validate type independently

Use **Power BI Desktop / an authorized connected tabular engine** locally to
check each selected measure. Inspect the observed result type or evaluate the
measure with a valid filter context, including more than one context where the
type might vary. A model's offline TOM `Unknown` is NOT independent evidence.
An individual numeric-looking display or a format string is also not proof.

- `numeric`: independently confirmed scalar number in the contexts reviewed.
- `nonnumeric`: independently confirmed scalar text/Boolean/etc. not numeric.
- `variant`: the result genuinely varies by context/type, or cannot be assigned
  one stable coarse category without losing important information.
- `unknown`: no independent result/type established; do not guess.
- blank `observed_kind`: not reviewed yet.

A query can fail due to missing context/permissions or invalid expressions;
record `unknown` and private notes rather than claim that the analyzer is
wrong. Record any suspected misclassifications as local test cases with
**synthetic** formulas, never by copying company DAX into a public issue.

## Anonymous summary: accuracy **among conclusive reviewed samples only**

Run separately for each CSV after entering independent labels:

```powershell
foreach ($case in @('P01', 'P02')) {
    $csv = Join-Path $private "sm002-validation-$case.csv"
    $json = Join-Path $private "sm002-validation-$case-summary.json"
    python .\scripts\sm002-evidence-validation.py --review $csv --out $json
}
```

The tool rejects unexpected CSV headers, unknown labels, duplicate sample IDs,
multiple case IDs, Git-tree paths and overwrites. Summary JSON contains only
alias, aggregate counts and a ratio within the **conclusively reviewed** sample;
it never contains table/measure names, formulas, notes or private paths.

`observed_kind = unknown` or `variant` is **inconclusive**, not counted as
correct or incorrect. `conclusive_match_rate = null` means no conclusive
review. These rates are not model-wide accuracy estimates: selection is bounded
and stratified, and conclusive-only reporting can introduce selection bias.
One must report `sampled`, `reviewed`, `conclusive` and inconclusive counts next
to any rate, and avoid claiming validated SM-002 gate warnings.

**Review each JSON for privacy before sharing.** Small cells, case aliases or
aggregated values may indirectly identify a business. The safest default is to
share only top-level counts after approval.

## Acceptance criteria

1. Existing `inspect`, `audit`, JSON v1, SARIF and exit codes remain unchanged
   when evidence export is not requested.
2. The reviewer CSV is explicit, local-only and outside Git; no overwrites,
   raw expressions or secrets.
3. Sampling is reproducible for an unchanged model and independent from
   confirmed SM-002 coverage.
4. Synthetic tests exercise filtering, determinism, CSV escaping, argument
   validation, privacy, and correct conclusive/unknown counting.
5. Private reviewer confirms selected results independently; the team reports
   conclusive match rates and their sample sizes, NOT inferred overall accuracy.

## Run tests

```powershell
dotnet test -c Release --filter 'FullyQualifiedName~EvidenceValidationSampleWriterTests'
dotnet test -c Release
dotnet build -c Release -warnaserror
python -m unittest discover -s tests/pilot -p 'test_*.py' -v
git diff --check
```
