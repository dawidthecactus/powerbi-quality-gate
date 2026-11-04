# Structural example projects

The two examples here are **synthetic fixtures for `pbiq`**, not official Power BI exports.
They are designed to exercise the current structure validator. They are **not guaranteed
to open in Power BI Desktop**, and passing the scan does not certify PBIR/TMDL schema
correctness or DAX behavior.

## Valid structural example

From the repository root:

```powershell
dotnet run --project src/PowerBIQualityGate.Cli -- scan examples/valid-pbir-tmdl
$LASTEXITCODE  # expected: 0
```

Expected summary: `Report format: Pbir`, `Semantic model: Tmdl`, and
`STRUCTURE VALIDATION: PASS (not full schema validation)`.

## Invalid PBIP shortcut

```powershell
dotnet run --project src/PowerBIQualityGate.Cli -- scan examples/invalid-pbip-shortcut
$LASTEXITCODE  # expected: 1
```

Expected diagnostic: `PBI-002` (unsupported shortcut version).

Both projects are also covered by automated xUnit tests. For a later milestone,
we should add anonymized **real Power BI Desktop exports** as integration fixtures
with separate provenance and privacy checks.
