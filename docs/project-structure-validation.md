# Project structure validation (Milestone 01.4)

`pbiq scan <file.pbip | directory>` first discovers exactly one PBIP shortcut, then
checks its report and referenced semantic model.

## What is checked

- `.pbip` is readable JSON, version `1.0`, containing exactly one `artifacts[].report.path`.
- The report path resolves within the PBIP folder and contains `definition.pbir`.
- The report has **either** enhanced `definition/` (PBIR) **or** root `report.json` (PBIR-Legacy).
- Enhanced PBIR has `report.json`, `version.json`, `pages/`; each page folder has
  `page.json` and each visual folder has `visual.json`. JSON files checked here must parse.
- `definition.pbir` references **either** a local semantic model (`byPath`) **or**
  an external model (`byConnection` with `connectionString`).
- A local semantic model contains `definition.pbism` and **either** TMDL
  `definition/` with `database.tmdl` and `model.tmdl`, **or** TMSL `model.bim`.
- Report and model manifest versions must be `1.0` (legacy) or `4.0+`.

## Diagnostic codes

| Code | Condition |
| --- | --- |
| PBI-001 | PBIP path / JSON invalid or unreadable |
| PBI-002 | Invalid PBIP version |
| PBI-003 | Missing or ambiguous PBIP report artifact |
| PBI-004 | PBIP report reference is unsafe or invalid |
| PBI-005 | Referenced report folder missing |
| PBI-006 | Report manifest missing or invalid JSON |
| PBI-007 | Report format/version mismatch or unsupported version |
| PBI-008 | Missing or conflicting report payloads |
| PBI-009 | Missing or malformed enhanced PBIR file/folder |
| PBI-010 | Missing or malformed legacy report JSON |
| PBI-011 | Missing/ambiguous/invalid datasetReference |
| PBI-012 | Unsafe or invalid byPath model reference |
| PBI-013 | Referenced semantic model folder missing |
| PBI-014 | Semantic model manifest missing or invalid JSON |
| PBI-015 | Model format/version mismatch or unsupported version |
| PBI-016 | Missing or conflicting TMDL/TMSL payloads |
| PBI-017 | Required TMDL root file missing |
| PBI-018 | TMSL model.bim missing or malformed JSON |

## Exit codes

- `0`: structure checks passed.
- `1`: PBIP found, structure validation errors.
- `2`: input path/discovery failure (from Milestone 01.3).

## Explicit limitations

This is **structural validation**, not Microsoft JSON Schema compliance,
complete PBIR correctness, TOM/TMDL parsing, data validation or DAX testing.
The validator is read-only and does not contact Power BI/Fabric.

This version intentionally requires a `.pbip` shortcut and one report artifact.
Fabric Git exports without `.pbip`, multi-report workspaces and standalone `.pbir`
entry points are future work. Model references must stay in the PBIP root. The
path-containment check is lexical; it is not a hardened filesystem sandbox for
malicious symlinks/junctions.

The tests create **synthetic structural fixtures**. They are not guaranteed to
open in Power BI Desktop. A real PBIP export should be used for integration tests
in a later milestone.
