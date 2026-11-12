# SM-002.5c.2: private Excel CSV review interoperability

## Problem and safety goal

After saving a dependency-review CSV in a localized Excel installation,
its rows may be serialized with a different separator or quote layout. The
previous SM-002.5c.1 `summarize` command intentionally rejected these inputs
instead of guessing the meaning of shifted fields.

`sm002-review-excel.py` provides two **explicit, local-only** operations:

- `prepare`: read the untouched eight-column private PBIQ review CSV and
  create a semicolon-delimited UTF-8 BOM CSV with an Excel `sep=;` directive.
- `normalize`: read the original canonical CSV **and** a spreadsheet-edited CSV,
  check all six candidate-identity fields exactly for every candidate, then
  copy only `review_status` and `notes` into a new canonical UTF-8 comma CSV.

Both commands reject changes to candidate identities, missing/duplicate rows,
unsupported statuses, unexpected columns, malformed quotation, and ambiguous
single-cell serialization. They never overwrite an existing file or write
under a Git worktree. A safe, limited recovery path exists for spreadsheet
rows that become a single quoted cell containing all eight fields. A
header-only empty-candidate review (P02) is supported.

**The original export is authoritative**. If Excel has reformatted a measure,
table, or reference name, `normalize` fails. It does not try to infer a new
identity based on approximate matching. Re-open the prepared file via Excel's
Text/CSV import with all identifier columns set to Text and save again.

## Private workflow

Use the existing unedited source in a private folder outside Git.

```powershell
Set-Location "$HOME\Desktop\Porfolio\powerbi-quality-gate"
$private = Join-Path $HOME 'PBIQ Pilot Private'
$source = Join-Path $private 'sm0025b-P01-dependencies.csv'
$excel  = Join-Path $private 'sm0025c2-P01-for-excel.csv'
$normal = Join-Path $private 'sm0025c2-P01-normalized.csv'

python .\scripts\sm002-review-excel.py prepare `
    --source $source --out $excel --dry-run
python .\scripts\sm002-review-excel.py prepare `
    --source $source --out $excel

# Open $excel privately, edit review_status (and optional private notes),
# save it as UTF-8 CSV, then close Excel before importing.

python .\scripts\sm002-review-excel.py normalize `
    --source $source --edited $excel --out $normal --dry-run
python .\scripts\sm002-review-excel.py normalize `
    --source $source --edited $excel --out $normal

python .\scripts\sm002-dependency-review.py summarize `
    --review $normal --case-id P01 `
    --out (Join-Path $private 'sm0025c2-P01-summary.json')
```

All output paths must be new. Never stage private CSVs, model files, DAX,
identifiers, or private review notes in Git, attach them to a public PR, or
upload them to ChatGPT. The tool prints counts and constant labels only.

## Review labels are NOT engine proof

A `confirmed_missing` label means **a reviewer entered** that label. The
normalizer validates the CSV structure and identity, **not** the truth of the
reviewer's conclusion. Keep unverified references as `uncertain`, and record
any independent DAX/engine evidence privately. The existing `summarize`
output remains an aggregate of manual labels, not certified DAX errors.

## Limits

- The tool supports CSV, **not native XLSX**. UTF-8 (with/without BOM) only.
  It does not support automatic detection of arbitrary codepages.
- Excel may coerce some identifier strings into numbers or dates. Such
  modifications are rejected rather than silently accepted.
- `prepare` uses semicolon CSV and a `sep=;` directive for localized Excel;
  `normalize` also accepts a valid comma CSV or tab-separated export.
- Input size is limited to 16 MiB and 10,000 candidates per file.
- Python-only; no .NET code or public audit/Quality Gate semantics change.

## Tests

```powershell
dotnet test -c Release
dotnet build -c Release --no-incremental -warnaserror
python -m unittest discover -s tests/pilot -p 'test_*.py' -v
git diff --check
```

The 17 new tests use only synthetic identities and strings. The total Python
suite should contain 49 tests (one symlink test may be skipped on Windows).

Use the original SM-002.5b private export for a real-model pilot. Confirm
that `normalize` preserves candidate counts and remains silent about private
identifiers; no change to the DAX analyzer output is expected.
