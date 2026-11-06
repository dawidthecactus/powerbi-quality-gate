# Milestone 03.1: JSON Diagnostic Contract (v0.3.0 alpha)

This stage is the first step toward a GitHub-native Power BI linter. It is
**not** a stable v0.3.0 release. Public PBIQ v0.2.0-rc.1 stays unchanged.

## Changes

- Adds `pbiq audit <path> --format json` with explicit, strict schema v1.
- Preserves text, `--quiet`, `--details`, warning and coverage gate behavior.
- Reuses the SAME audit engine and policy: no duplicated rules, DAX parsing or
  invented findings.
- Emits JSON to stdout even on audited gate failures (exit `1`) or handled
  evaluation failures (exit `2`). The OS exit code still matters.
- Uses `location.objectPath` for semantic objects. `filePath` and `line` are
  **null**, not fabricated. Accurate coordinates belong to milestone 03.2.
- Adds 8 new xUnit cases and 6 end-to-end CLI smoke cases to Windows/Linux CI.
- Updates current branch to `0.3.0-alpha.1` only; public RC stays tagged.
- Updates README release status and experimental JSON docs link.

## Install in an isolated feature branch (PowerShell 7)

```powershell
Set-Location 'C:\Users\dawid\Desktop\Porfolio\powerbi-quality-gate'
git switch main
git pull --ff-only
git status --short
# Do not put uncommitted changes at risk. A clean tree is expected.
git switch -c feat/v0.3-json-contract

$archive = "$env:USERPROFILE\Downloads\pbiq_milestone_03_1_json_contract.zip"
Expand-Archive -LiteralPath $archive -DestinationPath . -Force
python scripts/verify-03-1-contract.py

git diff --check
git status --short
```

The ZIP contains **complete changed files**, not a patch script. It replaces
`AuditCommand.cs`, the CLI `.csproj`, `README.md` and `.github/workflows/ci.yml`
and adds the new renderer, tests, schema and scripts. **Start from the clean
`main` source at commit `80aa48f` / RC `v0.2.0-rc.1`** so other in-progress
changes are not overwritten. Run this only on the new feature branch.

## Local verification

```powershell
dotnet restore
dotnet build -c Release -warnaserror
dotnet test -c Release --no-build
dotnet format whitespace --verify-no-changes

./scripts/test-audit-json.ps1

# JSON is stdout. Print the result even when expected exit is 1.
$dll = 'src/PowerBIQualityGate.Cli/bin/Release/net10.0/pbiq.dll'
dotnet $dll audit examples/valid-pbir-tmdl --format json
$LASTEXITCODE

dotnet $dll audit examples/warning-pbir-tmdl --fail-on-warning --format json
$LASTEXITCODE

git diff --check
```

Expected: existing 355 tests plus eight new tests **if the original count has
not changed**, and all six JSON CLI smoke cases. The strict warning case is
supposed to exit 1; this is not a process crash.

The existing public `audit --quiet` output, screenshot workflow and release
contract should remain unchanged on the tagged 0.2.0 RC.

## Commit and CI

```powershell
git add README.md .github/workflows/ci.yml
git add src/PowerBIQualityGate.Cli/Commands/AuditCommand.cs
git add src/PowerBIQualityGate.Cli/PowerBIQualityGate.Cli.csproj
git add src/PowerBIQualityGate.Cli/Rendering/AuditJsonRenderer.cs
git add tests/PowerBIQualityGate.Tests/AuditJsonContractTests.cs
git add docs/audit-json-contract-v1.md docs/schemas/audit-report.v1.schema.json
git add docs/milestone-03.1-json-contract.md
git add scripts/verify-03-1-contract.py scripts/test-audit-json.ps1

git diff --cached --check
git diff --cached --stat

git commit -m 'feat(cli): introduce versioned audit JSON contract'
git push -u origin feat/v0.3-json-contract

# CI is configured for main/PR/manual dispatch, not every feature branch push.
gh workflow run ci.yml --ref feat/v0.3-json-contract
$runId = (gh run list --workflow ci.yml --limit 1 --json databaseId | ConvertFrom-Json)[0].databaseId
gh run watch $runId
```

Do **not** create tag `v0.3.0`, merge to main, or overwrite the existing
`v0.2.0-rc.1` tag. Stable v0.2.0 should be tested/released independently.

## Acceptance criteria

- [ ] Existing text-mode tests remain green on Windows and Ubuntu.
- [ ] All new JSON tests and real CLI smoke cases pass.
- [ ] Output is exactly one JSON document on stdout for handled cases.
- [ ] Exit codes 0/1/2 agree between JSON and process status.
- [ ] Unknown SM-002 types remain review items, not false warnings.
- [ ] No fabricated TMDL source file/line locations or raw DAX leak.
- [ ] JSON Schema validates (schema syntax checked locally with Draft 2020-12).
- [ ] Public RC tag and release remain unchanged.

## Explicit limitations

This stage does not implement JSON for `scan`/`inspect`, SARIF, GitHub PR
annotations, `.pbiq.yml`, rule exclusions or Fabric repository discovery.
The System.CommandLine parser can produce non-JSON errors before the handler
runs. These are acknowledged and will receive attention in later DX hardening.
