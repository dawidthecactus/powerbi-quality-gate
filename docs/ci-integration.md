# PBIQ in CI: PBIP and Fabric Git

PBIQ audits **local checked-out** TMDL metadata. The published [v0.3.0-alpha.1](https://github.com/dawidthecactus/powerbi-quality-gate/releases/tag/v0.3.0-alpha.1) includes JSON (`schemaVersion: 1.0.0`), SARIF (`2.1.0`), `.pbiq.yml` and a composite GitHub Action. These features are **not included** in older `v0.2.0-rc.1` binaries. Pin the reviewed release commit `d9df0f82548334a9520da8731a22e97c7e3ee946` when using the alpha action in an external repository.

## Exit codes are the policy contract

| Exit | Meaning | Consumer action |
| --- | --- | --- |
| `0` | Audit evaluated and accepted, possibly with advisory findings / coverage gaps | Allow |
| `1` | Policy rejected findings or prerequisite structure failed | Block |
| `2` | Not evaluated: unsupported input, invalid policy, parser/TOM/engine failure | Block/investigate |

Warnings are advisory by default. `--fail-on-warning` and
`--min-measure-coverage 95` are independent, explicit gates. An offline
`Unknown` measure type does not automatically imply a formatting defect.

## Run from a built source checkout

```powershell
$cli = './src/PowerBIQualityGate.Cli/bin/Release/net10.0/pbiq.dll'

# Portable, one-line status; input from a checked-out PBIP repo.
dotnet $cli audit ./examples/warning-pbir-tmdl --fail-on-warning --quiet
$exit = $LASTEXITCODE
if ($exit -ne 0) { throw "PBIQ blocked the build (exit $exit)" }

# Native Fabric Git model without .pbip shortcut.
dotnet $cli audit ./examples/fabric-git/Revenue.SemanticModel --format json
```

## GitHub composite Action

A real consumer repository can use the action declared by the **root**
[`action.yml`](../action.yml). See the
[copyable consumer workflow](examples/pbiq-consumer-workflow.yml) and
[03.5 implementation guide](milestone-03.5-reusable-action.md).

```yaml
- uses: actions/checkout@v7
- name: Run PBIQ
  id: pbiq
  continue-on-error: true
  uses: dawidthecactus/powerbi-quality-gate@REPLACE_WITH_REVIEWED_FULL_COMMIT_SHA
  with:
    project-path: reports/YourReport.pbip
    # Fabric alternative: workspace/Revenue.SemanticModel
    fail-on-warning: 'true'
    sarif-output: reports/pbiq.sarif
- name: Upload SARIF
  if: ${{ always() && steps.pbiq.outputs.evaluated == 'true' && steps.pbiq.outputs.sarif-path != '' }}
  uses: github/codeql-action/upload-sarif@v4
  with:
    sarif_file: ${{ steps.pbiq.outputs.sarif-path }}
    category: pbiq-powerbi
- name: Enforce gate after upload
  if: ${{ always() && steps.pbiq.outputs.exit-code != '0' }}
  shell: bash
  run: exit 1
```

Add `contents: read` and `security-events: write` to the job permissions if
uploading into an entitled GitHub Code Scanning repository. Upload may be
unavailable for untrusted fork PRs; never elevate untrusted code by moving it
to a privileged `pull_request_target` flow. If Code Scanning is unavailable,
retain the SARIF as an ordinary CI artifact instead.

The action builds PBIQ at its **pinned action revision**, runs SARIF audit,
exposes `exit-code`, `sarif-path`, and `evaluated`, and then fails when the exit is nonzero.
`continue-on-error: true` is intentionally used **only** to preserve the SARIF
for upload **only when `evaluated=true`**; the final gate step enforces the saved result.
An exit `1` may also indicate structural validation failure, so do not gate uploads
solely by exit code. An unsuccessful SARIF invocation must never replace an earlier
Code Scanning result set with an empty report. Do not omit the
final gate step. The action currently rebuilds the .NET CLI on every call.

Before the v0.3 feature is released, replace the placeholder with an **actual
reviewed full commit SHA containing `action.yml`**. The earlier public
`v0.2.0-rc.1` tag does not contain the action.

## Direct CLI SARIF export

For integrations without the composite Action:

```bash
set +e
./pbiq audit workspace/Revenue.SemanticModel \
  --format sarif --sarif-source-root "$GITHUB_WORKSPACE" > pbiq-results.sarif
status=$?
set -e
# Retain SARIF for diagnostics. Upload to Code Scanning only when
# .runs[0].invocations[0].executionSuccessful is true; then enforce $status.
```

The source root must actually contain the PBIP or Fabric item directory.
Unmapped results remain SARIF findings without physical locations and cannot
be annotated to a line. JSON and SARIF contain model object identifiers; do
not upload reports from private customer projects to public repositories.

## Scope

PBIQ does **not** refresh datasets, validate DAX runtime behavior or connect
to Power BI/Fabric services. Metadata/structure checks are conservative and
not a semantic correctness certificate. GitHub Code Scanning annotations
require repository entitlement and correctly mapped source files. The composite action passed Windows and Linux CI, an independent consumer-repository acceptance test, and real GitHub Code Scanning SARIF upload using synthetic Fabric Git models. This does not guarantee every downstream repository supports Code Scanning.
