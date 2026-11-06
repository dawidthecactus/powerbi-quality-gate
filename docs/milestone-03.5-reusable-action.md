# PBIQ v0.3 alpha: Milestone 03.5 · Reusable GitHub Action

This stage introduces a root-level `action.yml` composite GitHub Action.
This is experimental source-branch functionality, **not a published stable
v0.3 release**. Consumers must review and pin a full commit SHA once a
suitable v0.3+ revision is available. The existing `v0.2.0-rc.1` assets do
not support SARIF or the YAML configuration engine.

## Scope

- Requires `actions/checkout` in the **consumer** workflow.
- Uses `actions/setup-dotnet@v6` to supply .NET 10, restores and builds the
  PBIQ CLI **from the action's pinned repository revision** (not from the
  consumer repository). Thus the CLI implementation matches the action
  revision even before a published v0.3 binary release exists.
- Invokes only `pbiq audit --format sarif --sarif-source-root <workspace>`.
- Inputs `project-path`, `config-path`, `fail-on-warning`,
  `min-measure-coverage`, and `sarif-output` are explicit; no user input is
  interpolated directly into a shell command. Paths must be relative to the
  caller's GitHub workspace and cannot lexically escape it.
- An omitted `config-path` preserves normal `.pbiq.yml` auto-discovery
  beside the discovered PBIP or Fabric `.SemanticModel` item. Native Fabric
  Git audit support is introduced in milestone 03.6.
- `exit-code` is 0 (accepted), 1 (gate failed) or 2 (not evaluated / setup
  problem). `sarif-path` is an absolute file path when a valid SARIF document
  was produced, even for exit code 1 or 2; otherwise it is empty.
- A quality-gate failure **fails the action** after writing both outputs.
  Do not interpret a zero-result SARIF as a passing gate: check `exit-code`.
- SARIF files are not uploaded automatically. Upload is an explicit workflow
  step with `security-events: write` and Code Scanning entitlement.
- The action does not inspect DAX content, run model code, use credentials or
  modify PBIP files. SARIF **may** contain object names and remediation text.
  Review customer/project confidentiality before uploading anywhere.

## Consumer workflow

See [`docs/examples/pbiq-consumer-workflow.yml`](examples/pbiq-consumer-workflow.yml).

The workflow uses `continue-on-error: true` on the action step so an exit 1
report remains available for `upload-sarif`. It then enforces the saved
`exit-code` in a separate step. A direct caller that does not need SARIF
upload can omit `continue-on-error` and fail the job immediately.

The example's `@REPLACE_WITH_REVIEWED_FULL_COMMIT_SHA` **must be replaced**
with a full immutable commit SHA from a reviewed, accessible revision. Do not
pin the current `v0.2.0-rc.1`: it does not contain this feature. Action
references in this repository's CI use `uses: ./` only to test the
unpublished source revision.

### Permissions and fork PR safety

`upload-sarif` requires GitHub Code Scanning support and write permission
for `security-events`. For untrusted fork PRs, such permission may not be
available. Never solve this by exposing secrets or using privileged
`pull_request_target` to run untrusted checkout code. If Code Scanning is
not enabled, use an ordinary report artifact instead of upload-sarif.

### File path boundaries

- Source paths inside SARIF are relative to the checkout root, so PBIPs in
  nested project directories map to actual repository paths.
- `project-path`, `config-path` and `sarif-output` accept only workspace-
  relative paths. Neither symlinks nor junctions are resolved for containment;
  repositories with adversarial links require additional hardening.
- `sarif-output` overwrites an existing report at that location. Choose an
  output path that does not overwrite a tracked source file.
- PowerShell Core (`pwsh`) must exist on the runner. Hosted Windows and
  Ubuntu runners include it; self-hosted runners must provide it.

## Verification

From the 03.5 feature branch:

```powershell
dotnet restore
dotnet build -c Release -warnaserror
dotnet test -c Release --no-build
dotnet format whitespace --verify-no-changes
./scripts/test-audit-json.ps1
./scripts/test-audit-sarif.ps1
python scripts/verify-03-5-action.py
git diff --check
```

Expected current unit-test baseline: **392**. There are no added xUnit
cases in this milestone; new end-to-end action behavior is exercised by the
Windows/Ubuntu GitHub Actions CI matrix with a passing fixture and an
intentionally failing warning-policy fixture. Its job must remain green while
asserting `exit-code=1` and that the action step itself reports failure.

The action wrapper should be released and referenced by a full SHA before
advertising it as ready for external adoption.
