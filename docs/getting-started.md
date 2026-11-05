# Getting started with PBIQ

PBIQ (Power BI Quality Gate) is a local, **offline, read-only** command-line tool for Power BI Project (`.pbip`) files. Start with `scan` to validate the project layout, use `inspect` for local TMDL metadata, and run `audit` to review four semantic-model quality rules.

> **Release status:** source currently targets `0.2.0-rc.1`. The Windows x64 and Linux x64 packages have passed an automated release dry run, but **no official v0.2.0 GitHub Release has been published**. If the [Releases page](https://github.com/dawidthecactus/powerbi-quality-gate/releases) is empty, build from source or, for evaluation, download an artifact from a successful [Release workflow](https://github.com/dawidthecactus/powerbi-quality-gate/actions/workflows/release.yml). Workflow artifacts expire and may require GitHub sign-in.

## Requirements and supported inputs

- **From source:** Git and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- **Standalone package:** Windows x64 or Linux x64, with no .NET runtime/SDK needed on the machine running PBIQ. macOS and ARM64 packages are not available yet.
- **Input:** a `.pbip` file, or a directory containing exactly one `.pbip` entry point. `inspect` and `audit` require a **local TMDL semantic model**. A standalone `.pbir`, TMSL-only model, remote semantic-model connection, or Fabric export without `.pbip` is not currently supported.
- **Permissions:** readable local project files. PBIQ does not need Power BI Service credentials and does not refresh or write to model files.

## Path A: build from source (available today)

Run in **PowerShell 7** on Windows, from any working directory:

```powershell
git clone https://github.com/dawidthecactus/powerbi-quality-gate.git
Set-Location powerbi-quality-gate

dotnet restore
dotnet build -c Release -warnaserror

# A synthetic, public PBIP fixture. No Power BI Desktop required.
dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- scan examples/valid-pbir-tmdl
$LASTEXITCODE  # 0
```

On Linux, the same `dotnet` commands work in Bash. Check a native exit code with `echo $?` instead of `$LASTEXITCODE`.

To avoid typing `dotnet run`, after building use the application DLL directly:

```powershell
dotnet ./src/PowerBIQualityGate.Cli/bin/Release/net10.0/pbiq.dll --help
```

This still requires .NET on the machine. The standalone binaries below do not.

## Path B: use a standalone Windows x64 package

When an official release is available, download **`pbiq-win-x64.zip`** and **`SHA256SUMS.txt`** from [Releases](https://github.com/dawidthecactus/powerbi-quality-gate/releases). Until then, use only a reviewed dry-run artifact for testing. Run these commands in **PowerShell** from the directory containing the downloaded files:

```powershell
# Optional but recommended: compare the archive hash to SHA256SUMS.txt.
Get-FileHash -Algorithm SHA256 ./pbiq-win-x64.zip
Get-Content ./SHA256SUMS.txt

Expand-Archive -LiteralPath ./pbiq-win-x64.zip -DestinationPath ./pbiq -Force
& ./pbiq/pbiq.exe --version
& ./pbiq/pbiq.exe --help
```

Check that the SHA-256 digest matches the line for `pbiq-win-x64.zip` before running the executable. A matching checksum verifies the downloaded bytes against the published manifest, not the publisher's identity.

## Path C: use a standalone Linux x64 package

When available, download **`pbiq-linux-x64.tar.gz`** and **`SHA256SUMS.txt`** into the same folder:

```bash
grep 'pbiq-linux-x64.tar.gz' SHA256SUMS.txt | sha256sum -c -
mkdir -p pbiq
tar -xzf pbiq-linux-x64.tar.gz -C pbiq
./pbiq/pbiq --version
./pbiq/pbiq --help
```

The checksum file covers **both** Windows and Linux archives. The command above verifies the Linux archive only; if both archives are present, you can instead run `sha256sum -c SHA256SUMS.txt`.

## Your first three commands

Examples below assume `pbiq` resolves to the installed executable (or replace `pbiq` with `& ./pbiq/pbiq.exe` in PowerShell, `./pbiq/pbiq` on Linux, or `dotnet ./src/PowerBIQualityGate.Cli/bin/Release/net10.0/pbiq.dll` from a source checkout).

```text
pbiq scan <path-to-file-or-directory>
pbiq inspect <path-to-file-or-directory> --measures
pbiq audit <path-to-file-or-directory>
```

For a **copy-pasteable source checkout** demonstration in PowerShell:

```powershell
$cli = './src/PowerBIQualityGate.Cli/bin/Release/net10.0/pbiq.dll'
$sample = './examples/valid-pbir-tmdl'

dotnet $cli scan $sample
$LASTEXITCODE                 # 0: structural checks passed

dotnet $cli inspect $sample --measures
$LASTEXITCODE                 # 0: metadata inventory completed

dotnet $cli audit $sample --quiet
$LASTEXITCODE                 # 0: no blocking finding; the sample has coverage gaps
```

Use the example model for onboarding only. It is **synthetic** and not guaranteed to open or refresh in Power BI Desktop.

## Run PBIQ against your own PBIP

```powershell
# Standalone binary; replace with the real path to YOUR project:
$exe = 'C:\Tools\PBIQ\pbiq.exe'
$project = 'C:\Reports\MyProject.pbip'

& $exe scan $project
& $exe inspect $project --measures
& $exe audit $project --details
```

`--details` may reveal object names and locations; use it locally and review the output before sharing. To collect a CI-friendly, single-line verdict instead:

```powershell
& $exe audit $project --quiet
$LASTEXITCODE
```

## Common problems

| Symptom | Likely explanation | Next step |
| --- | --- | --- |
| `PROJECT DISCOVERY FAILED`, exit `2` | File/folder missing, unreadable, or ambiguous `.pbip` entry points | Point to an existing `.pbip` file or its single-PBIP folder |
| Structural validation failed, exit `1` | Broken PBIP/report/model reference or invalid implemented structure check | Run `scan` and review its `PBI-` diagnostics |
| `LOCAL TMDL REQUIRED`, exit `2` | Model is TMSL-only or external instead of local TMDL | Use a PBIP project with local TMDL definition files |
| `PASS WITH COVERAGE GAPS`, exit `0` | Some measure types are `Unknown`/unsupported in offline TOM | Inspect metadata; optionally enforce `--min-measure-coverage` in CI |
| `FAIL (COVERAGE POLICY)`, exit `1` | Explicit classification threshold not met | Review the threshold and unresolved types; do not interpret as proof of bad formatting |
| Question marks where icons should be | Terminal font/Unicode fallback | Read the textual verdict and exit code, which remain authoritative |

## Continue reading

- [CLI command reference](commands.md): arguments, modes, flags, and exit codes.
- [Reading reports](output-guide.md): severity, coverage, and `PASS` vs `FAIL`.
- [CI integration](ci-integration.md): local automation and GitHub Actions.
- [Release process](release-process.md): dry runs, prerelease tags, and checksum verification.
- [Media capture plan](readme-media-plan-02.5.3.1.md): reproducible color screenshots and GIFs using synthetic projects only.

PBIQ does **not** validate DAX expression correctness, refresh data, or certify Power BI Desktop compatibility. A passing audit covers only the implemented rules.
