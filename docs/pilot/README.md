# Real-model pilot: PBIQ v0.3.0-alpha.1

## Goal

Test the **published** PBIQ alpha on **3 to 5 real PBIP/Fabric Git models** without sharing models or sensitive findings. This is a compatibility, accuracy, reproducibility and UX evaluation, not a production gate rollout. Do not use a company/client model without authorization under your organization's data handling policies.

**Recommended selection:** P01 = smaller PBIP; P02 = medium PBIP with common DAX and relationships; P03 = large/complex PBIP; P04 = a native Fabric Git `.SemanticModel` if accessible; P05 = edge case (many relationships, measures or intended technical columns). The pilot accepts **any 3–5 valid cases**, and an all-PBIP run is acceptable if no real Fabric Git model is available. Record whether each input is PBIP or Fabric. Never publish actual model names.

## Setup on Windows (PowerShell 7)

Requirements: Python 3.12+ and the **published** standalone PBIQ v0.3.0-alpha.1. Use the exact executable installed and verified during the [5-minute Quick Start](../quick-start.md). No need to install anything in Power BI Desktop or upload reports.

From your checked-out PBIQ source repository on a new pilot branch:

```powershell
Set-Location "$HOME\Desktop\Porfolio\powerbi-quality-gate"

# Create a PRIVATE working directory OUTSIDE the Git repository.
# Prefer local, non-synced storage approved for handling your data.
$private = Join-Path $HOME 'PBIQ Pilot Private'
New-Item -ItemType Directory -Force $private | Out-Null

Copy-Item '.\docs\pilot\manifest.template.csv' (Join-Path $private 'manifest.csv')
notepad (Join-Path $private 'manifest.csv')
```

Edit the three placeholder paths in the private manifest to actual **local** `.pbip` files/folders or `.SemanticModel` directories. Keep IDs exactly `P01`, `P02`, `P03` (optionally `P04`, `P05`). Use the `kind` value `pbip` or `fabric`. CSV paths containing commas must be enclosed in double quotes. Do **not** copy the manifest into Git. `manifest.csv` is never uploaded by the script.

Find the previously installed binary, for example the verified copy from your local release acceptance. **Set `$pbiqExe` to its real absolute path**. Don't set it to a `.dll` or a source-built development CLI.

```powershell
$pbiqExe = 'C:\Path\To\Installed\pbiq.exe'  # CHANGE THIS
$run = Join-Path $private 'run-01'

python .\scripts\pbiq-pilot.py run `
  --manifest (Join-Path $private 'manifest.csv') `
  --pbiq $pbiqExe `
  --out $run `
  --repeat 2 `
  --timeout 180

# EXPECTED LOCAL-ONLY OUTPUTS:
# run-01/private-raw/       raw JSON and console output, potentially sensitive
# run-01/private-review.csv names/messages; PRIVATE manual classification
# run-01/sanitized-summary.json aggregated statistics; review before sharing
```

The tool checks that neither the manifest nor output directory is in **any Git working tree**, and rejects output folders that already exist. It checks the installed PBIQ version, runs `scan` and `inspect --measures` for PBIP inputs, runs `audit --format json` for every input, and repeats audits to flag inconsistent results. It never changes the original PBIP/TMDL files.

**Interpretation:** `scan` failure on a real PBIP should be captured and investigated. `audit` exit `1` is *not by itself* proof that quality rules ran: check `evaluated`. `audit` exit `2` means a setup/format/tool failure. A `repeat_consistent=false` result merits investigation. Runtime and model size are recorded as **numbers**, not identifying names.

## Manual false-positive review

Open `private-review.csv` **locally** (Excel is fine) and review **every row** against the source model and actual design intent. The sheet contains technical object paths and text; never upload it to GitHub or ChatGPT.

Fill the `classification` column with **exactly one** of:

- `true_positive`: genuine design concern accurately identified by PBIQ.
- `intentional`: finding is accurate, but design/usage intentionally deviates from the rule (a candidate for an explicit `.pbiq.yml` exclusion, **not** a false alarm).
- `false_positive`: finding is factually incorrect or identifies an object that does not match the rule's stated criteria.
- `uncertain`: insufficient evidence; needs manual or expert review.

Add private context to `notes` if useful; **do not delete rows**. If there are no findings, the review CSV contains only a header. Be particularly cautious interpreting `SM-002` when offline TOM marks measure types `Unknown`.

Then run:

```powershell
python .\scripts\pbiq-pilot.py summarize --out $run

# These two files should contain only aliases/aggregates, not paths or names.
Get-Content (Join-Path $run 'sanitized-summary.json')
Get-Content (Join-Path $run 'sanitized-review.json')
```

The summarizer reports reviewer-classified counts per rule and flags unreviewed findings. **Only share `sanitized-summary.json` and `sanitized-review.json` after personally checking that even alias IDs or aggregates cannot identify a confidential project.** Do not post the manifest, `private-raw`, or `private-review.csv`.

## Acceptance and decision gate

Record outcomes separately:

| Dimension | Pilot evidence | Next step when abnormal |
| --- | --- | --- |
| Compatibility | `scan_exit_code`, `inspect_exit_code`, `evaluated`, `failure_code` | Reproduce on a permitted synthetic minimal model; open an issue |
| Quality | Counts of `true_positive`, `false_positive`, `intentional`, `uncertain` per rule | Proposed bug fix vs precise exclusion vs improved docs |
| Coverage | Measure type unknowns, classification percent and gap flags | Avoid claiming an SM-002 defect with unknown type |
| Stability | `repeat_consistent` for each project | Isolate nondeterminism, save private reproduction |
| Performance | `audit_seconds` and model size counts | Examine slower projects, compare repeated local timings |
| Workflow UX | Did a new reviewer understand status/exit code and actionability? | Improve Quick Start, docs or diagnostic text |

The pilot is complete when **3–5 models were attempted**, every failure has a disposition, each finding has a classification (or explicit `uncertain`), results are inspected for confidentiality, and there is a reasoned priority list for alpha.2. It's fine for compatibility to fail: that is exactly the feedback this pilot is designed to uncover. Do not change the rule engine or rewrite the published v0.3.0-alpha.1 tag during the pilot.

## Regression tests for the harness

```powershell
python -m unittest discover -s tests/pilot -p 'test_*.py' -v
```

Tests use only synthetic, mocked PBIQ JSON and temporary directories. They never load real PBIP models. After reviewing the pilot's anonymized results, use them to plan **step 3: alpha.2 backlog**. Do not publish pilot source documents in a public issue.
