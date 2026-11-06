# PBIQ release process and gates

The GitHub workflow `.github/workflows/release.yml` builds self-contained,
single-file **untrimmed** .NET 10 executables for Windows x64 and Linux x64.
It verifies extracted archives and SHA-256 checksums, not merely the build
workspace. macOS/ARM64 artifacts are not currently provided.

## Release states at the start of 03.7 stabilisation

- **Published:** `v0.2.0-rc.1` is a public release candidate with **pre-v0.3**
  CLI behavior. Stable `v0.2.0` has not been released.
- **Development:** the CLI project declares `0.3.0-alpha.1`, with JSON, YAML,
  SARIF, GitHub Action and native Fabric Git input. An implementation passing
  branch CI is **not yet a published release**.
- **Never rewrite tags** or imply newer features exist in the RC binary.

## 1. Before the v0.3 alpha tag

1. Complete and review 03.1–03.6, including release-readiness docs and
   package-smoke improvements. Run build with warnings-as-errors, 406+ xUnit
   tests, JSON/SARIF/Fabric smoke and source-contract checks.
2. Open a **single PR** from the consolidated 03.7 branch to `main`. Require
   Windows and Ubuntu CI, review all changed files and reconcile user docs.
   The linear feature-branch ancestry needs **no six-way merge**.
3. Independently test the composite Action with an **external consumer repo**
   using a reviewed SHA and synthetic PBIP and Fabric models. Verify exit 0/1/2,
   SARIF preservation and source locations; upload to Code Scanning only when
   safe and entitled. Do not claim this is tested until completed.
4. Run release **dry run** from the tested feature branch or PR head:

   ```powershell
   gh workflow run release.yml --ref chore/v0.3-release-readiness
   gh run list --workflow release.yml --limit 5
   gh run watch <RUN_ID>
   ```

   `workflow_dispatch` uploads artifacts **without creating a GitHub Release**.
   Require success of `metadata`, both `Verify` jobs, both `Package` jobs,
   and checksums. The package smoke tests should cover text, JSON, SARIF,
   configuration and Fabric Git **using extracted standalone executables**.
5. Download the dry-run artifacts and verify Windows binary/sha256 **outside
   the source checkout**, including a directory with spaces in its name.
6. Merge the reviewed PR to `main`, rerun both normal CI and release dry run
   on `main`, and inspect final release notes and the `--version` output.

## 2. Tag only after explicit approval

Only after the release gates are green, and only when `main` contains the
checked-in `<Version>0.3.0-alpha.1</Version>`:

```powershell
git switch main
git pull --ff-only
git status --short
git tag -a v0.3.0-alpha.1 -m "PBIQ v0.3.0 alpha 1"
git push origin v0.3.0-alpha.1
```

**Do not run these tag commands during the stabilisation branch stage.**
The tag workflow validates that `v<Version>` matches the CLI project and
that the tagged commit is reachable from `origin/main`. A pre-release tag
is published as a GitHub **prerelease**, not a stable release.

## 3. Artifact verification

Expected files:

- `pbiq-win-x64.zip`
- `pbiq-linux-x64.tar.gz`
- `SHA256SUMS.txt`

Verify the published hashes against the manifest:

```powershell
Get-FileHash -Algorithm SHA256 .\pbiq-win-x64.zip
Get-Content .\SHA256SUMS.txt
```

```bash
sha256sum -c SHA256SUMS.txt
```

The checksum proves consistency against the published manifest, not signer
identity. Keep private PBIP/TMDL files and customer metadata out of public
release archives and release notes.

## 4. Future stable release

`v0.3.0-alpha.1` remains prerelease. A future stable version requires its
own explicit SemVer bump, tested commit, CI and release dry run followed by
approval and a **new immutable tag**. A successful PBIQ audit certifies only
its implemented metadata policy, not data refresh or DAX correctness.

For historical v0.2.0 release readiness, see
[release-readiness-02.5.1-2.md](release-readiness-02.5.1-2.md).
