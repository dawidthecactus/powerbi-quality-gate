# PBIQ release process: v0.2.0

The release workflow is `.github/workflows/release.yml`. The current candidate
is `0.2.0-rc.1` (in the CLI `.csproj`). Release archives are .NET 10,
self-contained, single-file, untrimmed executables for Windows x64 and Linux
x64. No .NET SDK or runtime is required by the installed app.

For the 02.5.1/02.5.2 readiness changes and smoke cases, see
[release readiness](release-readiness-02.5.1-2.md).

## 1. Before creating a tag

1. Commit the tested code and check normal CI on `main` is green on both OSes.
2. Trigger a manual **dry run**, which generates artifacts but publishes no
   release:

   ```powershell
   gh workflow run release.yml --ref main
   gh run list --workflow release.yml --limit 3
   gh run watch <RUN_ID>
   ```

3. Require success in: `metadata`, both `Verify` jobs, both `Package` jobs and
   `Verify SHA256 artifacts`. Package jobs unpack their archives and run real
   executables against the public examples, including expected error exits.
4. Download the dry-run packages and SHA256 file, confirm checksums, and try
   the Windows executable from outside your source checkout.
5. Do not add private PBIP, measure names, TMDL or validation CSVs to releases.

## 2. RC tag (only after all RC checks)

While `<Version>0.2.0-rc.1</Version>` is checked in and `main` is clean:

```powershell
git switch main
git pull --ff-only
git status --short
git tag -a v0.2.0-rc.1 -m "PBIQ v0.2.0 release candidate 1"
git push origin v0.2.0-rc.1
```

The workflow checks the tag matches `<Version>`, that the tagged commit is
reachable from `main`, reruns all checks, and uploads a **GitHub prerelease**:

- `pbiq-win-x64.zip`
- `pbiq-linux-x64.tar.gz`
- `SHA256SUMS.txt`

Check it with `gh release view v0.2.0-rc.1`. Do not retarget tags.

## 3. Stable v0.2.0 (future gated step)

After RC installation checks, release notes and any fixes have been verified:

1. Set `<Version>0.2.0</Version>` in
   `src/PowerBIQualityGate.Cli/PowerBIQualityGate.Cli.csproj`.
2. Run local build/test/format and the full **manual release dry run** again.
3. Commit the version bump to `main` and require green CI.
4. Only then create `v0.2.0` from the current main commit and push that tag.

The workflow publishes a **normal GitHub Release** for `v0.2.0`, not a
prerelease. Tagging is intentionally a separate user-approved action.

## 4. Check downloaded packages

On Linux, download all three release assets into the same directory:

```bash
sha256sum -c SHA256SUMS.txt
```

On Windows, check the archive hash against the matching line in the checksum
file:

```powershell
Get-FileHash -Algorithm SHA256 .\pbiq-win-x64.zip
Get-Content .\SHA256SUMS.txt
```

A checksum is a corruption check, not a cryptographic signature of the
publisher. A passing `pbiq audit` covers only the limited metadata rules; it
does not execute DAX or validate a full Power BI refresh.

## Supported runtime / policy

- Windows x64 and Linux x64 only, no macOS/ARM64 yet.
- `PublishTrimmed=false` for TOM compatibility.
- `workflow_dispatch` produces artifacts only, never publishes a Release.
- The workflow has read-only permissions except for the tag-only publish job.
- Tags are immutable release references. Do not move an existing tag.
