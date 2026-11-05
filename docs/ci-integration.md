# PBIQ in CI and developer workflows

PBIQ returns machine-consumable exit codes while preserving a concise human-readable report. **Choose your gate policy explicitly.** By default, Warning findings are advisory; `--fail-on-warning` and `--min-measure-coverage` make them or incomplete type classification blocking.

> **Release status:** there is no published stable `v0.2.0` yet. The examples below build a reviewed source revision, rather than linking to a nonexistent release download. Once a tagged package is published and verified, a separate workflow may install that binary instead.

## Exit-code contract

| Code | `audit` meaning | CI decision |
| :--: | --- | --- |
| `0` | Evaluated rules passed the configured gate (possibly with advisories/coverage gaps) | Allow |
| `1` | Rule/policy failure, or prerequisite structural failure | Block |
| `2` | Bad input/flags, unsupported model, TOM/rule/integrity failure or no evaluated rules | Block; investigate tooling/input |

A `PASS WITH COVERAGE GAPS` exit `0` is **not equivalent to 100% type classification**. Set a meaningful minimum explicitly if the pipeline requires it. No minimum is enforced by default.

## Quick local gate: PowerShell 7

From a built source checkout:

```powershell
$cli = './src/PowerBIQualityGate.Cli/bin/Release/net10.0/pbiq.dll'
$pbip = './examples/warning-pbir-tmdl'

# Default is advisory, so this synthetic Warning still returns 0.
dotnet $cli audit $pbip --quiet
$LASTEXITCODE

# Strict warnings block. The synthetic model returns exit 1.
dotnet $cli audit $pbip --fail-on-warning --quiet
$exit = $LASTEXITCODE
if ($exit -ne 0) {
    throw "PBIQ quality gate blocked this change (exit $exit)."
}
```

For a genuine project, replace the example folder with your `.pbip` path. `--quiet` prints a single plain-text line and avoids model object names.

## Quick local gate: Bash

```bash
set -euo pipefail
PBIQ='./pbiq/pbiq'  # path to installed Linux x64 executable
PBIP='./reports/MyReport.pbip'

"$PBIQ" audit "$PBIP" --fail-on-warning --quiet
```

With `set -e`, either `1` or `2` stops the pipeline. If you need different messages for policy failures versus tool errors, temporarily capture the status with `if ...; then ...; else status=$?; ...; fi` rather than suppressing all failures with `|| true`.

## GitHub Actions: build a pinned PBIQ revision

This example assumes your repository **already contains** the PBIP files under `reports/MyReport.pbip` and uses a Linux runner. Pin the tool to a **reviewed commit** instead of silently tracking a changing `main`. The SHA below is a reviewed working source revision from the release-readiness stage; update it deliberately for subsequent PBIQ changes.

Create `.github/workflows/powerbi-quality.yml` in **your Power BI project repository**:

```yaml
name: Power BI Quality Gate

on:
  pull_request:
  workflow_dispatch:

permissions:
  contents: read

jobs:
  pbiq:
    runs-on: ubuntu-latest
    steps:
      - name: Checkout PBIP repository
        uses: actions/checkout@v7

      - name: Install .NET 10 SDK
        uses: actions/setup-dotnet@v6
        with:
          dotnet-version: '10.0.x'

      - name: Checkout pinned PBIQ source
        shell: bash
        run: |
          git clone https://github.com/dawidthecactus/powerbi-quality-gate.git .pbiq-tool
          git -C .pbiq-tool checkout 39968aa5469f99125b416c1e43258db8887fbfba

      - name: Build PBIQ
        shell: bash
        run: dotnet build .pbiq-tool/src/PowerBIQualityGate.Cli/PowerBIQualityGate.Cli.csproj -c Release -warnaserror

      - name: Audit semantic model
        shell: bash
        run: |
          dotnet .pbiq-tool/src/PowerBIQualityGate.Cli/bin/Release/net10.0/pbiq.dll \
            audit ./reports/MyReport.pbip --fail-on-warning --no-color --quiet
```

**Why not copy the GitHub Actions workflow from PBIQ directly?** That workflow tests and packages **PBIQ itself**. The sample above is a **consumer workflow** that runs PBIQ against a different repository's PBIP.

The final `dotnet ... audit` command returns `1` on a blocking finding and `2` on an evaluation problem, causing the Bash job to fail. Modify `--fail-on-warning` and `--min-measure-coverage` to match your team's policy. For example:

```bash
dotnet .pbiq-tool/src/PowerBIQualityGate.Cli/bin/Release/net10.0/pbiq.dll \
  audit ./reports/MyReport.pbip --fail-on-warning --min-measure-coverage 95 --quiet
```

**Important:** for a PBIP with offline TOM `Unknown` measure types, the threshold may reject the model even when no formatting defects are confirmed. Treat this as an intentional policy decision, not as a silent bug in the report.

## Windows PowerShell CI considerations

To run a standalone Windows binary in a GitHub Actions PowerShell step:

```yaml
- name: Audit PBIP (Windows)
  shell: pwsh
  run: |
    $exe = './tools/pbiq/pbiq.exe'
    $project = './reports/MyReport.pbip'
    & $exe audit $project --fail-on-warning --quiet
    $code = $LASTEXITCODE
    if ($code -ne 0) { throw "PBIQ audit failed (exit $code)" }
```

The preceding step assumes you have **already downloaded and extracted** a trusted, verified Windows package to `tools/pbiq`. It is intentionally not a complete workflow while there is no published release asset. In PowerShell, native commands may behave differently under `$PSNativeCommandUseErrorActionPreference`; where a script must **inspect an expected nonzero exit** instead of failing immediately, use `Start-Process -Wait -PassThru` with redirected output, as PBIQ's own cross-platform regression workflows do.

## Choosing the right policy

| Goal | Suggested options | Effect |
| --- | --- | --- |
| Local exploration | `audit <path>` | Shows findings; warnings advisory |
| PR review summary | `audit <path> --quiet` | One-line status without object names |
| Block confirmed warnings | `audit <path> --fail-on-warning --quiet` | Warning triggers exit `1` |
| Block coverage gaps | `audit <path> --min-measure-coverage 95 --quiet` | Classification below 95% triggers exit `1` |
| Both conditions | Combine both policy flags | Either can block; warning policy can take display precedence |
| Investigate locally | `audit <path> --details` | Object locations and HOW/WHY; may contain sensitive metadata |

## CI safety and diagnostics

- Use a **fixed source commit** or verified release; do not download arbitrary binaries or execute scripts from unreviewed branches.
- Never push private PBIP/TMDL files, detailed audit logs, TOM previews or customer data into public CI fixtures. Running on a private GitHub runner still requires checking log and artifact retention policies.
- Use `--no-color` for deterministic plain logs. This flag **does not anonymize model paths**; use `--quiet` for a minimal audit verdict.
- Do not use `continue-on-error: true` or `|| true` for a step that is intended to enforce the quality gate.
- Treat `2` as a **failure to evaluate**, not as a passing model.
- PBIQ never executes DAX or connects to Power BI Service. Passing the gate does not prove business correctness, refresh ability or Desktop compatibility.

## Further reading

- [Getting started](getting-started.md)
- [CLI commands and flags](commands.md)
- [Interpreting statuses and coverage](output-guide.md)
- [Current quality rules](first-rule-pack.md)
- [Coverage policy details](coverage-aware-gate-02.4.3.md)
- [PBIQ's own CI smoke tests](quality-gate-ci.md)
