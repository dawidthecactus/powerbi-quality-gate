# Milestone 02.5.3.1: README redesign and authentic media contract

## Goal

Make the repository front page immediately understandable to Power BI analysts,
developers, and open-source contributors, while keeping the documentation
truthful about the pre-release state and SM-002 coverage gaps.

**This patch changes documentation only.** It does not alter CLI behavior,
banner styling, gates, model inspection, packaging or release tags.

## README information architecture

1. Centered PBIQ hero: concise claim, CI, platform, .NET, license, RC badges.
2. Honest early-access note: source `0.2.0-rc.1`, CI artifacts vs published release.
3. One-line audit example and brief coverage caveat.
4. Fast source quickstart, with future standalone install in collapsible sections.
5. Commands overview with accurately scoped flags.
6. Quality gate policy, semantic-model rule names and exit-code contracts.
7. Safety and limitations, including privacy and read-only scope.
8. Navigation to existing detailed docs, contribution and license.

This order emphasizes user value and first run before implementation history.
Detailed milestone narratives remain in `docs/` and are not copied into README.

## Media work in 02.5.3.3 and 02.5.3.4

**Do not publish placeholders or synthesized terminal outputs as screenshots.**
Capture the real packaged Windows binary or a locally built CLI on the **public
synthetic fixtures**. Use color-capable terminal output: no `--no-color`,
no `NO_COLOR`, and no redirected output. For preview, the actual version and
model data shown must match the executable used.

Recommended assets, added only after verification:

| Path to create later | Source command / scenario | Use |
| --- | --- | --- |
| `assets/screenshots/scan-color.png` | `pbiq scan examples/valid-pbir-tmdl` | README hero image |
| `assets/screenshots/inspect-measures.png` | `pbiq inspect examples/valid-pbir-tmdl --measures` | Inspector section or docs (text is plain by design) |
| `assets/screenshots/audit-summary-color.png` | `pbiq audit examples/valid-pbir-tmdl` | Explain PASS WITH COVERAGE GAPS |
| `assets/screenshots/audit-warning-color.png` | `pbiq audit examples/warning-pbir-tmdl --fail-on-warning` | Show policy failure; expected exit `1` |
| `assets/demos/pbiq-quick-tour.gif` | Short sequence of `scan`, `inspect`, `audit` | Optional README hero animation |
| `assets/vhs/pbiq-quick-tour.tape` | Reproducible VHS recording script | Versioned source of GIF |

Initially the README contained **a comment marking the media slot**, without
broken image links. In 02.5.3.5 that slot is replaced with four linked
PNG captures already committed to `assets/screenshots/`. For example:

```markdown
![PBIQ scan displaying a color-coded structural validation report for a public sample project](assets/screenshots/scan-color.png)
```

If a GIF is approved for the hero, prefer a short 8–15-second recording with
an immediately recognizable output state. Keep a static scan image as a
fallback for readers with reduced-motion preferences. If using HTML, provide
proper `alt` text and a static alternative.

### Capture checklist

- [ ] Use only `examples/valid-pbir-tmdl` and `examples/warning-pbir-tmdl`.
- [ ] Show **real** output from the checked-in binary, not edited command text.
- [ ] Record in a color-capable terminal; do not use `--no-color`.
- [ ] Check the terminal profile, font, cursor, window title and prompt for
      private paths, addresses, account names or secrets.
- [ ] For hero, crop to terminal only; avoid private shell history or other apps.
- [ ] Hide unrelated status widgets, personal device/profile info or tokens.
- [ ] Fix any `?` glyph fallback issue before final capture when practical.
- [ ] Address version mismatch in the CLI scan banner vs `--version` before RC.
- [ ] Avoid repeated large pixel dimensions; target legibility on mobile.
- [ ] GIF uses short duration and sane file size; include a static alternative.
- [ ] Verify PNG/GIF render on GitHub (light and dark mode) and use relative links.
- [ ] Run `git diff --check` and inspect **all binary assets** before commit.

### Suggested visual treatment

- Dark terminal background with crisp, ANSI-color highlights.
- A green/cactus accent in the shell prompt, not artificially painted over
  the CLI's actual output. No misleading success or failure styling.
- `scan` shows the large original PBIQ ASCII banner; `inspect --measures` and
  `audit --quiet` intentionally remain concise and need no giant logo.
- Keep filenames consistent and avoid embedding private models or business data.

## Acceptance for 02.5.3.1

- [x] README foregrounds the three main commands and caveats.
- [x] Documentation paths refer to existing repository files plus this plan.
- [x] No unreleased v0.2.0 download is advertised as available.
- [x] No missing media is referenced by GitHub-rendered Markdown.
- [x] Color screenshots/GIF plan is documented for the next submilestones.
- [ ] User runs docs sanity checks and CI after commit.

## Local docs-only validation

```powershell
# After overlaying the ZIP from the repository root:
git diff --check
git diff -- README.md
Test-Path docs/readme-media-plan-02.5.3.1.md

dotnet build -c Release -warnaserror
dotnet test -c Release --no-build
dotnet format whitespace --verify-no-changes
```

Commit using `docs(readme): redesign product landing and plan terminal media`.
No release tag should be created during this submilestone.

## Status after 02.5.3.5

The four screenshots are now committed and visible in the README (a main scan
image plus a collapsible three-image gallery). The images were taken from the
public synthetic examples and cropped or presentation-adjusted for legibility.
The CLI banner now obtains the SemVer base from the informational assembly
version, so future recordings should show the prerelease suffix automatically.

`python3 scripts/verify-release-docs.py` checks screenshot links, file headers,
local document links, and current CLI version text. A dedicated Docs check
workflow runs this check on pushes and pull requests. Future VHS re-recordings
should be visually reviewed before replacing the committed PNGs; they need not
be added merely because the workflow was invoked.

No GIF is included in this milestone. If an animated demo is later introduced,
use real recorded terminal output rather than a simulated animation.
