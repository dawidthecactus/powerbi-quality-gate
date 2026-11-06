# Milestone 02.5.3.5: Final Documentation & Release Polish

## Release status

This is a **pre-release polish patch**, not a tagged release. The `.csproj`
version remains `0.2.0-rc.1`. **Do not push `v0.2.0-rc.1` or `v0.2.0`** until
local tests and the fresh Windows/Linux Release dry run have completed.

## What changed

1. The branded `scan` header now uses `AssemblyInformationalVersionAttribute`
   (without optional `+SHA` build metadata), rather than truncating prerelease
   identifiers using `AssemblyVersion.ToString(3)`. With this candidate it
   displays `v0.2.0-rc.1`; `--version` can additionally include `+commit`.
2. The release smoke-test checks the **full** expected SemVer when invoking
   `pbiq --version`, and also checks that `scan --no-color` contains the exact
   short SemVer. This guards the extracted standalone binary on both OSes.
3. README now embeds the four existing public synthetic-fixture screenshots
   (one main image, three in a collapsible gallery). It does not link to an
   unavailable GIF or an unpublished stable GitHub Release.
4. A new standard-library documentation check validates local links and
   image file headers, checks that the media placeholder is gone, and checks
   that the README mentions the actual CLI version. It runs in a lightweight
   Docs check workflow on main/PR and in the Release workflow before packaging.
5. The historical README media plan now reflects actual screenshots.

## Local acceptance (Windows / PowerShell 7)

From the repository root, after applying the patch:

```powershell
git diff --check
python scripts/verify-release-docs.py
python scripts/check-vhs-tapes.py assets/vhs

dotnet restore
dotnet build -c Release -warnaserror
dotnet test -c Release --no-build
dotnet format whitespace --verify-no-changes

$dll = 'src/PowerBIQualityGate.Cli/bin/Release/net10.0/pbiq.dll'
$version = (& dotnet $dll --version) -join "`n"
$scan = (& dotnet $dll scan examples/valid-pbir-tmdl --no-color) -join "`n"
if ($LASTEXITCODE -ne 0) { throw 'Scan failed' }
if (-not $version.Contains('0.2.0-rc.1')) { throw "Bad CLI version: $version" }
if (-not $scan.Contains('POWER BI QUALITY GATE  v0.2.0-rc.1')) { throw 'Scan banner version mismatch' }
```

The test project should now report **355 tests** if the previous baseline
was 352 and the three new cases are discovered. GitHub Actions is the
source of truth for cross-platform builds; a static documentation check is
not a replacement for the actual CLI smoke test.

## Tag gate

After this patch is committed on `main` and CI + Docs check are green:

```powershell
gh workflow run release.yml --ref main
$runId = (gh run list --workflow release.yml --limit 1 --json databaseId | ConvertFrom-Json)[0].databaseId
gh run watch $runId
```

Confirm `metadata`, Windows/Ubuntu `Verify`, both `Package` jobs, and
`Verify SHA256 artifacts` all pass. Download and smoke-test Windows and Linux
archives. `workflow_dispatch` never publishes a Release or creates a tag.

Only then begin **02.5.4 RC**, following [release-process.md](release-process.md).

## Known release boundaries

- `scan` checks implemented PBIP structure, not entire JSON Schema/DAX.
- `inspect` and `audit` require local TMDL and an `.pbip` entry point.
- Unknown TOM measure types remain review gaps; SM-002 does not guess.
- Supported standalone downloads are Windows x64 and Linux x64.
- Screenshot images were captured from synthetic projects and cropped or
  presentation-adjusted; they are not a substitute for an executable smoke test.
