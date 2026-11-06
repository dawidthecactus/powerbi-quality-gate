# PBIQ v0.3 alpha · Milestone 03.6: native Fabric Git semantic models

**Status:** Experimental implementation for `feat/v0.3-fabric-git`, based on
commit `852da00` of `feat/v0.3-reusable-action`. Do not advertise this as a
published release before tests, pull requests and release preparation.

## Why this matters

Microsoft Fabric Git integration stores each semantic model as an item folder,
for example `Revenue.SemanticModel`, containing `definition.pbism` and a
`definition/` directory with TMDL files. The Git repository does **not** need
a `.pbip` shortcut or a `.Report` item for a semantic model to be valid.

Official documentation:

- https://learn.microsoft.com/en-us/fabric/cicd/git-integration/source-code-format
- https://learn.microsoft.com/en-us/power-bi/developer/projects/projects-dataset

## Supported entry points for `pbiq audit`

```pwsh
# A standalone Fabric Git semantic model item; no .pbip required:
pbiq audit workspace/Revenue.SemanticModel --format json

# The workspace folder works when it contains exactly one .SemanticModel item
# and no .pbip shortcuts:
pbiq audit workspace --format json

# Location-aware GitHub Code Scanning upload:
pbiq audit workspace/Revenue.SemanticModel --format sarif --sarif-source-root .

# The composite action's project-path accepts the same item/workspace paths.
```

Existing PBIP paths and legacy CLI commands remain unchanged.

### Discovery precedence (nonrecursive, fail-closed)

1. A `.pbip` file or exactly one `.pbip` in the supplied directory retains
   existing PBIP discovery and validation semantics.
2. If the supplied directory itself ends with `.SemanticModel`, treat it as
   one explicit Fabric semantic model item.
3. If no `.pbip` is present, a directory with **one immediate** child ending
   with `.SemanticModel` is treated as a Fabric Git workspace.
4. Multiple `.pbip` files or multiple `.SemanticModel` folders are ambiguous:
   exit 2, no arbitrary selection. Point at one item explicitly.
5. No recursive search: choose the workspace or item directory if the Git
   repository contains several workspaces/subfolders.

A Fabric Git item requires a readable `definition.pbism` with version >= 4,
`definition/database.tmdl`, `definition/model.tmdl` and `definition/tables/`
with at least one `.tmdl` table. TMSL-only `model.bim` is not supported by
`audit`. Invalid or conflicting definitions cause a nonzero result. The
existing Microsoft TOM/TMDL deserializer still decides model semantics.

**No temporary `.pbip` files are created** and input files are never edited.

## Config and output compatibility

- `.pbiq.yml` auto-discovery occurs **beside** the `.SemanticModel` item
  directory (the containing Fabric workspace). An explicit `--config`
  overrides it. PBIP configuration discovery is unchanged.
- Existing `SM-001` through `SM-004`, exclusions, measure coverage policy and
  exit codes 0 / 1 / 2 are reused.
- JSON schemaVersion remains `1.0.0`; `project.name` is the Fabric item
  directory name (such as `Revenue.SemanticModel`), with no synthetic PBIP.
- JSON source paths are workspace-relative. SARIF paths are relative to the
  explicit `--sarif-source-root` checkout root, preserving GitHub annotations
  for files nested inside workspace folders.
- Suppression counts and policy failures retain earlier behavior.
- `scan` (PBIP/PBIR structural validation) and `inspect` are **not** expanded
  to Fabric entry points in this milestone; only `audit` and its composite
  GitHub Action are expanded. Neither Fabric's remote REST API nor live
  workspace authentication are included. This is a read-only local Git audit.
- This is lexical, not real-path containment: hostile symlinks/junctions
  are not yet covered by the security boundary.

## Public synthetic test fixtures

- `examples/fabric-git/Revenue.SemanticModel`: valid standalone TMDL item,
  no PBIP file, three informational SM-003 findings.
- `examples/fabric-git-warning/Warning.SemanticModel`: native TMDL item with
  a bidirectional relationship, expected warning-policy exit 1.

## Verification

```pwsh
dotnet restore
dotnet build -c Release -warnaserror
dotnet test -c Release --no-build
dotnet format whitespace --verify-no-changes
./scripts/test-audit-json.ps1
./scripts/test-audit-sarif.ps1
./scripts/test-audit-fabric.ps1
python scripts/verify-03-1-contract.py
python scripts/verify-03-5-action.py
python scripts/verify-03-6-fabric.py
git diff --check
```

**Expected:** 406 xUnit test cases (392 previous + 14 newly added), plus
Fabric smoke cases on Windows and Linux. The CI workflow also runs the
composite action against a Fabric item and tests a deliberate policy failure.

Build/test outcomes must be verified on a developer system and GitHub Actions
rather than inferred from patch generation.

## Next: v0.3 stabilisation

Once green, review overlapping work across 03.1–03.6 in a single feature
branch PR targeting `main`; run complete CI and release smoke; reconcile docs,
README, action pinning, code scan permissions and SemVer before an alpha tag.
