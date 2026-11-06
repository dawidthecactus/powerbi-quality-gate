# PBIQ v0.3.0 alpha: Milestone 03.4 · SARIF and GitHub Code Scanning

Experimental feature branch only. Requires the earlier 03.1 JSON contract,
03.2 source mapping, and 03.3 YAML configuration milestones.

## CLI

```powershell
dotnet run -c Release --project src/PowerBIQualityGate.Cli -- audit `
  examples/valid-pbir-tmdl --format sarif --sarif-source-root . `
  > results.sarif
```

- SARIF **2.1.0** is one JSON document on **stdout**. The existing `0/1/2`
  process exit codes and `--fail-on-warning`/coverage policies are unchanged.
- `--sarif-source-root <directory>` is available only with SARIF and must be
  an existing ancestor of the discovered PBIP directory. Prefer the GitHub
  repository checkout root. With no explicit root, paths remain relative to
  the PBIP directory and **may not map** to GitHub if the PBIP lives in a
  subdirectory of the repository.
- Uses only findings that remain **after** configured exact exclusions.
  Disabled rules are absent from the SARIF tool rules table. Suppressed
  findings are not reintroduced. No measures are reclassified.
- Results use `error`, `warning`, and `note` SARIF levels. File locations are
  created only when 03.2 can map a finding to a unique TMDL declaration.
  Unmapped results remain in SARIF **without** a physical location: GitHub
  cannot annotate these until mapping improves. `pbiqUnlocatedResults` in
  run properties records the count.
- On audit errors, a valid empty SARIF run is emitted with
  `invocations[0].executionSuccessful = false`. consumers **must check the
  process exit code and `invocations[0].executionSuccessful`**. Never upload
  an unsuccessful run as a replacement Code Scanning analysis, even if exit
  `1` denotes structural failure and the SARIF is valid and empty.
- SARIF includes object names and remediation text, but intentionally no DAX
  expressions or absolute local file paths. Review before uploading reports
  for private/customer models.

## GitHub Actions integration example

This is a **consumer workflow example**, not a workflow that should be enabled
on this tool's own repository. After installing/building PBIQ in the repository
that contains the Power BI project:

```yaml
name: Power BI quality scan
on: [push, pull_request]
permissions:
  contents: read
  security-events: write
jobs:
  pbiq:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
      # Install an appropriately pinned PBIQ build here.
      - name: Run PBIQ (always persist SARIF and exit code)
        id: audit
        shell: bash
        run: |
          set +e
          ./pbiq audit path/to/MyReport.pbip --format sarif \
            --sarif-source-root "$GITHUB_WORKSPACE" > pbiq-results.sarif
          code=$?
          echo "exit_code=$code" >> "$GITHUB_OUTPUT"
          if [[ -s pbiq-results.sarif ]] && jq -e '.runs[0].invocations[0].executionSuccessful == true' pbiq-results.sarif >/dev/null; then
            echo "evaluated=true" >> "$GITHUB_OUTPUT"
          else
            echo "evaluated=false" >> "$GITHUB_OUTPUT"
          fi
          exit 0
      - name: Upload annotations to GitHub Code Scanning
        if: always() && steps.audit.outputs.evaluated == 'true'
        uses: github/codeql-action/upload-sarif@v4
        with:
          sarif_file: pbiq-results.sarif
          category: pbiq-powerbi
      - name: Enforce PBIQ quality gate
        if: always() && steps.audit.outputs.exit_code != ''
        shell: bash
        run: exit "${{ steps.audit.outputs.exit_code }}"
```

`security-events: write` is required for uploading. The SARIF upload action
can compute partial fingerprints and produces GitHub Code Scanning alerts and
PR annotations for mapped results. Upload support depends on the repository's
GitHub Code Scanning entitlement. For untrusted fork pull requests, the
workflow may not receive `security-events: write`; configure a safe
trusted workflow policy without running untrusted code with elevated tokens.
The reusable GitHub Action wrapper is planned for 03.5.

Official documentation:
- https://docs.github.com/en/code-security/reference/code-scanning/sarif-files/sarif-support
- https://docs.github.com/en/code-security/how-tos/find-and-fix-code-vulnerabilities/integrate-with-existing-tools/upload-sarif-file

## Verification

```powershell
dotnet restore
dotnet build -c Release -warnaserror
dotnet test -c Release --no-build
dotnet format whitespace --verify-no-changes
./scripts/test-audit-json.ps1
./scripts/test-audit-sarif.ps1
python scripts/verify-03-1-contract.py
git diff --check
```

Expected: **392 tests** (03.3 baseline 385 plus seven new tests).
Windows and Ubuntu CI must confirm.
