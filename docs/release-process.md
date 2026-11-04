# Release process (Milestone 01.8)

The release workflow is in `.github/workflows/release.yml`. It creates .NET 10
self-contained single-file executables for 64-bit Windows and Linux. No .NET SDK
or runtime is required on machines running these packages.

## Before the first tag

1. Merge the workflow and documentation changes into `main`.
2. Verify normal CI is green on Windows and Ubuntu.
3. Start a **dry run** of the new workflow:

   ```powershell
   gh workflow run release.yml --ref main
   gh run list --workflow release.yml --limit 3
   gh run watch <RUN_ID>
   ```

4. All `Verify` and `Package` jobs must pass. Inspect the run's artifacts:
   `pbiq-win-x64` and `pbiq-linux-x64`. A manual dry run does **not** create
   a GitHub Release.
5. Download and smoke-test the Windows archive outside the repository:

   ```powershell
   gh run download <RUN_ID> -n pbiq-win-x64 -D .\release-dry-run
   Expand-Archive .\release-dry-run\pbiq-win-x64.zip -DestinationPath .\release-dry-run\pbiq -Force
   & .\release-dry-run\pbiq\pbiq.exe --version
   & .\release-dry-run\pbiq\pbiq.exe scan .\examples\valid-pbir-tmdl
   $LASTEXITCODE  # expected: 0
   ```

   The `release-dry-run` directory is local test output. Do not commit it.

## Create a tagged prerelease

After the dry run is green, check the CLI project's `<Version>` matches the
planned tag. For the first release it is `0.1.0`.

```powershell
git switch main
git pull --ff-only
git status --short
git tag -a v0.1.0 -m "Power BI Quality Gate v0.1.0"
git push origin v0.1.0
```

Tag creation is an explicit manual step. Pushing `v0.1.0` triggers the same
checks and packages for that commit. Only a successful tag-triggered workflow
creates a **GitHub prerelease** with:

- `pbiq-win-x64.zip`
- `pbiq-linux-x64.tar.gz`
- `SHA256SUMS.txt`

Use `gh run list --workflow release.yml --limit 3` to find and track the release
run, then `gh release view v0.1.0` to confirm publication.

## Scope, compatibility, security

- Packages are for Windows x64 and Linux x64 only. macOS and ARM64 are not yet
  supported.
- `PublishTrimmed=false` avoids introducing trimming-related regressions in
  this early CLI version.
- The CLI performs **structural** PBIP validation; it does not validate DAX,
  semantic model correctness, Power BI Desktop compatibility, or performance.
- The current test fixtures are synthetic and are not guaranteed to open in
  Power BI Desktop.
- The workflow uses `contents: read` by default. Only the tag-triggered
  publish job receives `contents: write`.
- Manual `workflow_dispatch` runs build and upload reviewable artifacts but
  deliberately do not publish releases.
- GitHub Releases are versioned snapshots. Do not move an existing release tag
  to point to a different commit.

## Verify downloaded files

On Linux, download all three release assets into one directory and run:

```bash
sha256sum -c SHA256SUMS.txt
```

On Windows, compare `Get-FileHash -Algorithm SHA256 .\pbiq-win-x64.zip` with
the corresponding SHA256 value in `SHA256SUMS.txt`.
