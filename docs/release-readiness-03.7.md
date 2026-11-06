# PBIQ v0.3.0-alpha.1 · 03.7 Release Readiness

This milestone consolidates the six feature stages on one development branch.
The starting commit is `38203d8` (`feat/v0.3-fabric-git`); `main` still
contains the published `v0.2.0-rc.1` baseline.

## Included in the prospective alpha

| Stage | Capability |
| --- | --- |
| 03.1 | JSON audit diagnostic contract, schema version `1.0.0` |
| 03.2 | Verified TMDL locations for known SM rules, without fabricated coordinates |
| 03.3 | `.pbiq.yml` rule toggles, exact exclusions, warning/coverage policies |
| 03.4 | SARIF 2.1.0 and GitHub Code Scanning source paths |
| 03.5 | Composite GitHub Action with preserved SARIF and gate exit outputs |
| 03.6 | Native Fabric Git `.SemanticModel` folder auditing without synthetic PBIP |

## 03.7 changes

- README, Getting Started, command reference, CI guide and release guide
  describe the **implemented v0.3 source** and distinguish it from the
  **published v0.2 RC** binaries.
- Extended package smoke invokes the **extracted self-contained executable**
  to validate text modes, JSON status/schema, SARIF versions/source URIs,
  `.pbiq.yml` exclusions, Fabric Git and failure exit codes.
- CI and the release workflow run a static docs/source consistency check.
- `.gitattributes` normalizes YAML, PowerShell and Python files to LF.

## Remaining release gates

1. Local `dotnet build -c Release -warnaserror`, xUnit, CLI smoke, `dotnet format`.
2. Windows **and** Ubuntu CI on the 03.7 branch and on its PR.
3. Manual `release.yml` dry run on the branch and then `main`, checking
   re-extracted archives and SHA256 artifacts.
4. Standalone Windows binary tested outside the source checkout.
5. A **separate consumer repo** using the pinned composite Action, with
   successful, warning-policy and configuration-failure cases. Validate SARIF
   upload when repository permissions allow; do not infer Code Scanning
   support from local CI alone.
6. PR review and merge into `main`.
7. Only then, with explicit approval, create immutable `v0.3.0-alpha.1` tag.

## Known limits

- `scan`/`inspect` remain PBIP-only; `audit` accepts PBIP and local Fabric Git.
- No recursive search of a repository with multiple Fabric workspaces.
- No model refresh, DAX execution, Fabric REST authentication or data checks.
- A successful SARIF invocation must be distinguished from a structural
  validation failure before uploading. Gate on `evaluated=true`, not just exit 1.
- GitHub upload depends on Code Scanning entitlement and correct permissions;
  SARIF output does not by itself create PR annotations.
- Containment of source/config paths is lexical and not hardened against
  malicious symlinks/junctions. Use trusted checkouts.
- New JSON v1 fields such as `summary.suppressed` are still alpha-contract
  work; stable API compatibility is not promised until publication.

## Verification commands

```powershell
dotnet restore
dotnet build -c Release -warnaserror
dotnet test -c Release --no-build
dotnet format whitespace --verify-no-changes
.\scripts\test-audit-json.ps1
.\scripts\test-audit-sarif.ps1
.\scripts\test-audit-fabric.ps1
python scripts/verify-03-1-contract.py
python scripts/verify-03-5-action.py
python scripts/verify-03-6-fabric.py
python scripts/verify-release-docs.py
python scripts/verify-03-7-readiness.py
git diff --check
```

A green source build is **not** a release. The package, independent consumer
integration, PR and tag gates are separate and must be recorded explicitly.
