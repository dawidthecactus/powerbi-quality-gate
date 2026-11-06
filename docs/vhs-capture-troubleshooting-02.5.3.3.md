# 02.5.3.3.2: VHS viewport capture reliability

The first screenshot workflow failed on malformed regex delimiter syntax in
`Wait+Screen@30s /Measures inspected: 2/2/`; that was corrected in 02.5.3.3.1.
The second run then timed out on the long, successful `scan` report at
`Wait+Screen@30s /STRUCTURE VALIDATION: PASS/`.

[VHS issue #659](https://github.com/charmbracelet/vhs/issues/659) describes
screen-scope regex matching problems after terminal scrolling. The scan output
in the failed job contained the genuine PBIQ title, public project metadata and
structural checkpoints, so the problem was **synchronization while recording**,
not compilation, CLI semantics, or a quality-gate failure.

## Repair

- All four static PNG tapes avoid the vulnerable `Wait+Screen` command.
  They allow a bounded `Sleep 3s` after their visible command before `Screenshot`.
- A new CI step invokes the *real CLI process* with public synthetic fixtures
  and asserts the expected message **and the process exit code** in all four
  cases, including the deliberate `audit --fail-on-warning` exit `1`.
- The lightweight `scripts/check-vhs-tapes.py` requires the post-command
  settle and rejects `Wait` use in these static PNG tapes. It is not a VHS
  replacement; the workflow still invokes `vhs validate`.
- `scan.tape` now uses a taller 1500x1420 frame and a slightly smaller font to
  retain the terminal logo and final result in one screenshot. The PNG header
  verification dimensions are updated accordingly.

This is still an **unverified media candidate** until the GitHub screenshot
workflow uploads the PNGs and someone inspects them. `Sleep` is a reproducible
rendering pause, not proof that a terminal command succeeded. The separate
process checks protect CLI correctness; visual review protects the presentation.

## Run

```powershell
python scripts/check-vhs-tapes.py assets/vhs
# Expected: PASS: 4 VHS tapes passed static preflight

git diff --check
# Commit only these docs-media files after review.

gh workflow run docs-screenshots.yml --ref main
$runId = (gh run list --workflow docs-screenshots.yml --limit 1 --json databaseId | ConvertFrom-Json)[0].databaseId
gh run watch $runId
```

Never substitute customer PBIP content or manipulate PNG text. Fix any color,
clipping or icon fallback problem in the real tool or tape before importing
images into README.
