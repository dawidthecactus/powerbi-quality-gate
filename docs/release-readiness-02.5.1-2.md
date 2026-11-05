# PBIQ 02.5.1 + 02.5.2: Release readiness and distributable verification

## Baseline audit (before this patch)

At the end of 02.4.5, local xUnit and GitHub CI pass on Windows and Ubuntu.
The CLI includes scan, inspect (--tom/--measures) and audit (summary/details/quiet).
Release engineering nevertheless had gaps:

| Finding | Resolution in this patch |
| --- | --- |
| CLI still advertises `0.1.0` | Set the next candidate version to `0.2.0-rc.1` |
| Release workflow checked only `scan` inside the publish folder | Re-extract archives and smoke-test the **downloadable binary** with seven CLI cases |
| Checksum file existed only for a tagged release | Build and upload a checked `SHA256SUMS.txt` during **manual dry runs** too |
| Every release tag was `--prerelease` | Mark prerelease tags such as `v0.2.0-rc.1` as prerelease; use a normal GitHub Release for `v0.2.0` |
| Tag could be created on a divergent commit | Require the tagged commit to be reachable from `origin/main` |
| Only `0.1.0` appeared in versioning instructions | Document separate RC and stable gates |

No GitHub tag or release is created by applying these changes. `workflow_dispatch`
remains **artifact-only**; only an actual version-tag push can publish a Release.

## Candidate-version policy

The CLI's `<Version>` is `0.2.0-rc.1`. The workflow refuses mismatched tags:

- `v0.2.0-rc.1` is valid **only** when checked-in `<Version>` is `0.2.0-rc.1`.
- `v0.2.0` requires a subsequent, tested commit changing `<Version>` to `0.2.0`.
- The tagged commit must be reachable from the repository's `main` branch.
- No tag is created by a dry run. Do not move tags after publishing.

Use `v0.2.0-rc.1` only after a successful artifact-level dry run; this phase
**does not yet authorize a tag**.

## Dry-run gate (no release publication)

From a clean `main` after the ZIP patch has passed local tests and been pushed:

```powershell
gh workflow run release.yml --ref main
gh run list --workflow release.yml --limit 3
gh run watch <RUN_ID>
```

Required green jobs:

1. `Validate release version` (version and tag guard).
2. `Verify` on Ubuntu and Windows (`dotnet build`, xUnit, formatting).
3. `Package (win-x64)` and `Package (linux-x64)`, using standalone, self-contained,
   **untrimmed** .NET 10 binaries, after re-extracting each downloadable archive.
4. `Verify SHA256 artifacts` (downloads and checks both archives).

Each package smoke-test executes `pbiq` **directly**, not through `dotnet`, and
checks these public synthetic cases: `--version`, `scan`, `inspect --measures`,
`audit --quiet`, `audit --fail-on-warning --quiet`, `audit --min-measure-coverage 95
--quiet`, and invalid `--details --quiet`. Intentional exit codes `1` and `2`
are considered passing test cases when paired with the correct diagnostic.

Artifacts in a manual dry run:

- `pbiq-win-x64` containing `pbiq-win-x64.zip`;
- `pbiq-linux-x64` containing `pbiq-linux-x64.tar.gz`;
- `pbiq-checksums` containing `SHA256SUMS.txt`.

Manual runs intentionally do not create GitHub Releases.

## Consumer installation check

For Windows (do this **outside** the source repository so local SDK files do
not hide missing package dependencies):

```powershell
$runId = '<RUN_ID>'
$folder = Join-Path $env:TEMP 'pbiq-rc-check'
New-Item -ItemType Directory -Path $folder -Force | Out-Null
gh run download $runId -n pbiq-win-x64 -D $folder
Expand-Archive (Join-Path $folder 'pbiq-win-x64.zip') -DestinationPath (Join-Path $folder 'app') -Force
& (Join-Path $folder 'app/pbiq.exe') --version
& (Join-Path $folder 'app/pbiq.exe') audit 'C:\path\to\public\or\private\MyReport.pbip' --quiet
```

Use an existing PBIP path for the last command. `audit` can legitimately
return `0`, `1` or `2` based on its documented policies; merely launching the
binary does not imply a passing model. Prefer synthetic fixtures for shared logs.

The GitHub runner verifies the **archive** on Windows and Linux. A separate
human installation check on a machine without .NET installed is still useful,
because GitHub-hosted runners have the SDK installed.

## Security and known limits

- No release bundles private real-world models, validation CSVs or TMDL exports.
- Only public repository examples are used in smoke tests.
- `PublishTrimmed=false` is deliberate for TOM deserialization reliability.
- `Release` retains read-only permissions by default; only the tag-publish job
  can write release assets.
- `SHA256SUMS.txt` catches corruption; it is **not** a digital signature or
  provenance attestation.
- Windows x64/Linux x64 only. No ARM or macOS support in this milestone.
- `PASS WITH COVERAGE GAPS` and `Unknown` TOM measure types remain an explicit
  metadata-analysis limitation, not a quality certification.

## Local development gate

```powershell
dotnet clean -c Release
dotnet restore
dotnet build -c Release -warnaserror
dotnet test -c Release --no-build
dotnet format whitespace --verify-no-changes
git diff --check
```

Local xUnit test counts are unchanged by this patch. The extra package smoke
checks run in the **Release workflow** on both operating systems.

**Acceptance for 02.5.1 + 02.5.2:** both host package jobs, checksum job and
all verification jobs green in a manually dispatched Release workflow; manual
Windows install check performed before tagging the candidate.
