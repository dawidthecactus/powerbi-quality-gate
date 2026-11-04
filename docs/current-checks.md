# Current implemented checks (v0.1.0 development)

The scanner is read-only and validates the file and folder *structure* of a single PBIP project. It does not require Power BI Desktop, Microsoft Fabric, credentials, or network access.

## Entry point (discovery layer)

- Accept an explicit `.pbip` file or a directory containing exactly one `.pbip` file at its top level.
- Reject an empty or malformed path, a non-PBIP file, a missing path, inaccessible directories, or ambiguous directories with several `.pbip` shortcuts.
- Exit code `2` means the entry point could not be discovered.

## PBIP shortcut and report reference

| Code | Implemented structural check |
| --- | --- |
| PBI-001 | PBIP file is readable and parses as a JSON object. |
| PBI-002 | Shortcut `version` is the string `1.0`. |
| PBI-003 | `artifacts` is an array with exactly one item and a nonempty `artifacts[0].report.path`. |
| PBI-004 | Referenced report path is relative and remains lexically within the PBIP directory. |
| PBI-005 | Referenced report directory exists. |

## Report metadata and payload

| Code | Implemented structural check |
| --- | --- |
| PBI-006 | `definition.pbir` exists and is readable JSON object. |
| PBI-007 | Report version is `1.0` or at least `4.0`; enhanced PBIR requires `4.0+`. |
| PBI-008 | Exactly one report representation exists: `definition/` (enhanced PBIR) or root `report.json` (legacy). |
| PBI-009 | Enhanced PBIR includes JSON-object `definition/report.json`, `definition/version.json`, `definition/pages/`; `page.json` for each existing page folder; `visual.json` for each existing visual folder. |
| PBI-010 | Legacy root `report.json` exists and parses as a JSON object. |

**Important nuance:** The validator enumerates existing page and visual directories. It does not enforce that at least one page/visual exists, does not inspect JSON schemas, and does not render or evaluate visualizations.

## Semantic-model reference and metadata

| Code | Implemented structural check |
| --- | --- |
| PBI-011 | `definition.pbir` has a `datasetReference` object with exactly one of `byPath` or `byConnection`; a connection requires a nonempty `connectionString`. |
| PBI-012 | Local `byPath.path` is nonempty, relative and resolves lexically inside the PBIP directory. |
| PBI-013 | The referenced local semantic-model directory exists. |
| PBI-014 | Local `definition.pbism` exists and parses as a JSON object. |
| PBI-015 | Semantic-model version is `1.0` or `4.0+`; TMDL folder requires `4.0+`. |
| PBI-016 | Exactly one local model representation exists: `definition/` (TMDL) or `model.bim` (TMSL). |
| PBI-017 | TMDL root files `definition/database.tmdl` and `definition/model.tmdl` exist. |
| PBI-018 | TMSL `model.bim` exists and parses as a JSON object. |

`byConnection` (thin report) is accepted without a local semantic model, but the remote connection is **not tested**. TMDL files are checked for existence, not parsed.

## Exit codes

| Exit code | Meaning |
| --- | --- |
| `0` | Discovery succeeded; implemented structure checks found no errors. |
| `1` | PBIP found, but one or more structural validation errors. |
| `2` | Discovery/path failure, prior to structural validation. |

## Not implemented yet

- DAX syntax, logic, dependencies and performance.
- Model relationships, direction, ambiguity, many-to-many design, date tables, and cardinality.
- Column data types, hidden fields, numeric formats, measure descriptions, naming conventions.
- Full Microsoft JSON schema validation or parsing and semantic validation of TMDL/TMSL.
- Verifying semantic model existence on the Power BI service / Fabric, connectivity, refresh, permissions or data correctness.
- Report visual correctness, accessible design, layout quality, interactions or performance.
- A numeric quality score or configurable policy engine.
- Scan entry points without `.pbip`; multiple-report workspaces; hardened path confinement for symlinks/junctions.

The existing tests use synthetic fixtures, **not verified real-world PBIP exports**. A successful scan does not guarantee the project opens in Power BI Desktop.
