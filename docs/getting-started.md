# Getting started with PBIQ

PBIQ (Power BI Quality Gate) is an offline, read-only command-line tool for
Power BI projects and semantic-model metadata. It provides PBIP structure
checks (`scan`), TMDL/TOM inspection (`inspect`) and policy-based metadata
quality checks (`audit`).

> **Release status:** The already-published **v0.2 release candidate** is
> [`v0.2.0-rc.1`](https://github.com/dawidthecactus/powerbi-quality-gate/releases/tag/v0.2.0-rc.1).
> The **v0.3.0-alpha.1 source tree** adds Fabric Git input, JSON, SARIF,
> `.pbiq.yml` and a composite GitHub Action. Those additions are not part of
> the v0.2.0-rc.1 downloadable binaries. Until a v0.3 prerelease is tagged,
> check out the reviewed v0.3 source revision before following the new examples.

## Requirements and supported inputs

- **Source checkout:** Git and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).
- **Published standalone packages:** Windows x64 or Linux x64. No SDK or runtime is needed to run a self-contained package. macOS and ARM64 packages are not provided.
- **`scan` / `inspect`:** a `.pbip` file, or a directory containing exactly one `.pbip` file; `inspect` also needs local TMDL.
- **`audit`:** the same PBIP inputs, a native Fabric Git `.SemanticModel` item directory, or a directory with exactly one immediate `.SemanticModel` child and no `.pbip` shortcut. Ambiguous inputs are rejected.
- **Permissions:** only local file read access. No Fabric/Power BI Service login, model writeback or refresh is performed.

## Build and run from source

```powershell
# Clone the repository and check out the reviewed v0.3 branch/commit when
# testing features before their public prerelease.
git clone https://github.com/dawidthecactus/powerbi-quality-gate.git
Set-Location powerbi-quality-gate

dotnet restore
dotnet build -c Release -warnaserror

dotnet ./src/PowerBIQualityGate.Cli/bin/Release/net10.0/pbiq.dll --version
```

The examples below use this already-built DLL:

```powershell
$cli = './src/PowerBIQualityGate.Cli/bin/Release/net10.0/pbiq.dll'

dotnet $cli scan examples/valid-pbir-tmdl
dotnet $cli inspect examples/valid-pbir-tmdl --measures
dotnet $cli audit examples/valid-pbir-tmdl --quiet

# Native Fabric Git semantic model: no .pbip shortcut required.
dotnet $cli audit examples/fabric-git/Revenue.SemanticModel --format json

# Real source locations for GitHub Code Scanning: point to checkout root.
dotnet $cli audit examples/fabric-git/Revenue.SemanticModel `
    --format sarif --sarif-source-root .
```

The `examples/` models are **synthetic fixtures**, not production datasets and
not guaranteed to open or refresh in Power BI Desktop. CLI parser failures
(e.g. an unknown option) can use text errors even when a machine format is
requested. Automation must always inspect the process exit code.

## Configure quality policy

Create `.pbiq.yml` beside your `.pbip` file or, for Fabric Git, beside the
`.SemanticModel` item (inside its containing workspace):

```yaml
version: 1
rules:
  SM-001: true
  SM-002: true
  SM-003: true
  SM-004: false
policy:
  failOnWarning: true
  minMeasureCoverage: 0
exclusions:
  - rule: SM-003
    object: Sales[ProductId]
```

The config is optional and supports exact object exclusions, not wildcard
patterns. Use `--config path/to/.pbiq.yml` for an explicit file; an invalid
configuration rejects the audit (exit `2`). `--fail-on-warning` on the CLI
cannot be disabled by YAML; the CLI's coverage option overrides YAML.

## Standalone installation

The **currently published v0.2.0-rc.1** packages contain the earlier CLI
features only. Download a specific, reviewed release and its
`SHA256SUMS.txt` from [GitHub Releases](https://github.com/dawidthecactus/powerbi-quality-gate/releases).
Do **not** use the older package for the v0.3 options above.

### Windows x64

```powershell
Get-FileHash -Algorithm SHA256 ./pbiq-win-x64.zip
Get-Content ./SHA256SUMS.txt
Expand-Archive ./pbiq-win-x64.zip -DestinationPath ./pbiq -Force
& ./pbiq/pbiq.exe --version
```

### Linux x64

```bash
grep 'pbiq-linux-x64.tar.gz' SHA256SUMS.txt | sha256sum -c -
mkdir -p pbiq
tar -xzf pbiq-linux-x64.tar.gz -C pbiq
./pbiq/pbiq --version
```

A checksum detects mismatch against the published manifest; it does not
prove the publisher's identity. Binary release smoke tests validate the
archive's extracted executable separately from unit tests.

## Interpret outcomes

| Exit code | Meaning | In CI |
| --- | --- | --- |
| `0` | Evaluated; selected gate policy permits the findings (coverage gaps can still exist) | Continue |
| `1` | Policy or prerequisite structural failure | Block |
| `2` | Audit was not evaluated, bad input/configuration or tooling error | Block/investigate |

`PASS WITH COVERAGE GAPS` does **not** certify formatting or DAX correctness.
Unknown offline TOM data types remain review items. PBIQ does not execute DAX,
refresh data, validate all Microsoft schemas, or guarantee a model will work
in Power BI Desktop.

Machine outputs (`--format json` / `--format sarif`) include metadata object
names. Review confidentiality before uploading reports to GitHub Code Scanning
or public artifacts. Path-containment checks are lexical, not hardened
against malicious symlinks/junctions.

## Further reading

- [All CLI options and formats](commands.md)
- [JSON schema v1](audit-json-contract-v1.md)
- [SARIF and GitHub Code Scanning](milestone-03.4-sarif-github-annotations.md)
- [Consumer GitHub Action](milestone-03.5-reusable-action.md)
- [Native Fabric Git inputs](milestone-03.6-fabric-git.md)
- [CI integration](ci-integration.md)
- [Release policy](release-process.md)
