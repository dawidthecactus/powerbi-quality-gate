# Milestone 03.2: TMDL Source Location Mapping

Status: proposed v0.3.0-alpha implementation; **run Windows/Linux CI before merging**.

## Purpose

A TOM finding such as `Sales[ProductId]` can now be mapped to a verified
TMDL declaration, so CI consumers can navigate to the originating source:

```json
"location": {
  "objectPath": "Sales[ProductId]",
  "filePath": "Example.SemanticModel/definition/tables/Sales.tmdl",
  "line": 8
}
```

`schemaVersion` stays `1.0.0`: both nullable fields were already present in
03.1. The quality rules, gate policy, exit codes and text rendering are not
changed. Only `audit --format json` includes source coordinates.

## Algorithm

- TOM is still authoritative for measure, column, and relationship identity.
- A dedicated, read-only index enumerates `definition/tables/*.tmdl` and
  `definition/relationships.tmdl`.
- Only declarations at the table-member indentation or relationship top level
  count. Nested DAX lines do not count as source declarations.
- Single-quoted identifiers (including escaped apostrophes) are supported.
- A mapping is emitted only if the finding matches one TOM object and one
  TMDL declaration. Duplicate, unreadable, or missing declarations return null.
- Paths are relative to the `.pbip` directory, use `/`, and cannot contain
  `..` segments or be absolute. Lines are 1-based.
- The resolver does not invoke TMDL deserialization; Microsoft TOM still does
  that in the normal audit path. This is an indexing layer, **not a parser**.

## Supported rules

- `SM-001` and `SM-004`: declaration of the named relationship.
- `SM-002`: measure declaration (only when TOM confirms a numeric measure
  without a formatting string; classification behavior is unchanged).
- `SM-003`: column declaration.

## Known boundaries

- PBIR report visuals and `scan` structural diagnostics are not mapped here.
- Source positions identify a declaration start, not an exact property or DAX
  expression span. SARIF implementation follows in 03.4.
- Source path confinement is lexical, like current PBIQ discovery; symlinks
  and junctions are not hardened, and hostile repositories are out of scope.
- If a mismatch is detected, fields remain null; no guessed coordinate.
- The source resolver runs for JSON audit only; it does not modify `.tmdl`.

## Verification on both OSes

```powershell
python scripts/verify-03-1-contract.py
dotnet restore
dotnet build -c Release -warnaserror
dotnet test -c Release
dotnet format whitespace --verify-no-changes
.\scripts\test-audit-json.ps1
git diff --check
```

Expected count: **368 tests** (363 prior + five new resolver tests).
Test results must be confirmed by the developer, not inferred from patch
creation. Use GitHub Actions `workflow_dispatch` to test Windows and Ubuntu.
