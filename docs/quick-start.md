# PBIQ v0.3.0-alpha.1: 5-minute Quick Start

[Download the published alpha](https://github.com/dawidthecactus/powerbi-quality-gate/releases/tag/v0.3.0-alpha.1) | [CLI reference](commands.md) | [GitHub Actions](ci-integration.md)

PBIQ is an **offline, read-only** Power BI quality CLI. It scans PBIP structures (`scan`), inspects PBIP TMDL metadata (`inspect`), and audits PBIP or native Fabric Git `.SemanticModel` folders (`audit`). The standalone Windows x64 and Linux x64 releases need **no .NET installation**.

## 1. Windows x64 (PowerShell 7)

Run in a new directory:

```powershell
$ErrorActionPreference = 'Stop'
$base = 'https://github.com/dawidthecactus/powerbi-quality-gate/releases/download/v0.3.0-alpha.1'
New-Item -ItemType Directory -Force .\pbiq-trial | Out-Null
Set-Location .\pbiq-trial
Invoke-WebRequest "$base/pbiq-win-x64.zip" -OutFile pbiq-win-x64.zip
Invoke-WebRequest "$base/SHA256SUMS.txt" -OutFile SHA256SUMS.txt

$matches = @(Get-Content SHA256SUMS.txt | Where-Object { $_ -match '^[0-9a-fA-F]{64}\s+\*?pbiq-win-x64\.zip$' })
if ($matches.Count -ne 1) { throw 'Missing or ambiguous Windows checksum.' }
if ((Get-FileHash pbiq-win-x64.zip -Algorithm SHA256).Hash -ne $matches[0].Substring(0, 64)) {
    throw 'SHA256 mismatch.'
}
Expand-Archive pbiq-win-x64.zip -DestinationPath .\pbiq
& .\pbiq\pbiq.exe --version
```

The version should start with `0.3.0-alpha.1` and may include a build commit SHA.

**First audit without private data** (requires Git to obtain the public synthetic fixture):

```powershell
git clone --depth 1 --branch v0.3.0-alpha.1 https://github.com/dawidthecactus/powerbi-quality-gate.git fixtures
& .\pbiq\pbiq.exe audit .\fixtures\examples\fabric-git\Revenue.SemanticModel --quiet
$LASTEXITCODE  # expected 0
```

Or run on your own **local** model, without cloning the fixtures:

```powershell
& .\pbiq\pbiq.exe audit 'C:\Reports\Revenue.SemanticModel' --details
$LASTEXITCODE
```

For a traditional PBIP, use `audit 'C:\Reports\MyReport.pbip'` instead. Windows may warn about a new, unsigned executable. Verify the source and checksum before choosing to run it; do not disable security protections globally.

## 2. Linux x64 (bash)

```bash
set -e
mkdir -p pbiq-trial && cd pbiq-trial
base='https://github.com/dawidthecactus/powerbi-quality-gate/releases/download/v0.3.0-alpha.1'
curl -fL "$base/pbiq-linux-x64.tar.gz" -o pbiq-linux-x64.tar.gz
curl -fL "$base/SHA256SUMS.txt" -o SHA256SUMS.txt
grep 'pbiq-linux-x64.tar.gz$' SHA256SUMS.txt | sha256sum -c -
mkdir pbiq
tar -xzf pbiq-linux-x64.tar.gz -C pbiq
./pbiq/pbiq --version
git clone --depth 1 --branch v0.3.0-alpha.1 https://github.com/dawidthecactus/powerbi-quality-gate.git fixtures
./pbiq/pbiq audit ./fixtures/examples/fabric-git/Revenue.SemanticModel --quiet
```

For your own model: `./pbiq/pbiq audit ./workspace/Revenue.SemanticModel --details`. macOS and ARM64 standalone releases are **not yet available**.

## 3. Next commands

From the **Windows** example above, with `fixtures` checked out:

```powershell
# Scan and inspect: PBIP only
& .\pbiq\pbiq.exe scan .\fixtures\examples\valid-pbir-tmdl --no-color
& .\pbiq\pbiq.exe inspect .\fixtures\examples\valid-pbir-tmdl --measures

# JSON output
& .\pbiq\pbiq.exe audit .\fixtures\examples\fabric-git\Revenue.SemanticModel --format json > audit.json

# SARIF: paths relative to the checkout root
& .\pbiq\pbiq.exe audit .\fixtures\examples\fabric-git\Revenue.SemanticModel --format sarif --sarif-source-root .\fixtures > audit.sarif

# Deliberate warning-policy failure: expected exit 1
& .\pbiq\pbiq.exe audit .\fixtures\examples\fabric-git-warning\Warning.SemanticModel --fail-on-warning --quiet
$LASTEXITCODE
```

Machine formats (`--format json` and `--format sarif`) cannot be combined with `--quiet` or `--details`. Configure rules with optional `.pbiq.yml` beside the project or with `--config path/to/.pbiq.yml`.

## Interpreting the result

| Exit | Meaning |
| --- | --- |
| `0` | Audit completed; the selected policy allows the findings, possibly with advisories or coverage gaps. |
| `1` | Gate rejected findings **or** PBIP structure validation failed. |
| `2` | Input/configuration/tool error: audit was not evaluated. |

Current SM rules: `SM-001` bidirectional relationships, `SM-002` missing format on **confirmed numeric** visible measures, `SM-003` visible technical columns, `SM-004` inactive relationships. Unknown measure types are review items, not automatically warnings.

When uploading SARIF to GitHub Code Scanning, verify a **completed audit** (`runs[0].invocations[0].executionSuccessful` or GitHub Action's `evaluated` output); exit `1` alone cannot distinguish a rule failure from incomplete structural validation.

**Safety and limits:** This alpha does not refresh Power BI models, execute DAX or use Fabric credentials. JSON/SARIF and detailed text output may expose model object names. Review confidentiality before uploading. Path containment is lexical and is not hardened against malicious symlinks/junctions. Use trusted workspaces.

[Complete setup guide](getting-started.md) · [GitHub Action sample](examples/pbiq-consumer-workflow.yml) · [Report an issue](https://github.com/dawidthecactus/powerbi-quality-gate/issues)
