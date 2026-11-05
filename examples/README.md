# Synthetic PBIP examples

These are deliberately small **synthetic fixtures** for `pbiq`, not complete
Power BI Desktop exports. They do not contain real business data and are not
guaranteed to open or refresh in Power BI Desktop.

## Baseline structural/TOM example

```powershell
dotnet run --project src/PowerBIQualityGate.Cli -- scan examples/valid-pbir-tmdl
dotnet run --project src/PowerBIQualityGate.Cli -- audit examples/valid-pbir-tmdl
```

The structural scan passes and the TOM audit reports informational `SM-003`
findings for visible technical key columns. Exit code `0`.

## Warning policy example

```powershell
dotnet run --project src/PowerBIQualityGate.Cli -- audit examples/warning-pbir-tmdl
$LASTEXITCODE # expected 0: PASS WITH ADVISORIES

dotnet run --project src/PowerBIQualityGate.Cli -- audit examples/warning-pbir-tmdl --fail-on-warning
$LASTEXITCODE # expected 1: FAIL (WARNING POLICY)
```

This example duplicates the minimal baseline project and sets the
`Sales.ProductId` to `Product.ProductId` relationship to
`crossFilteringBehavior: bothDirections`. This intentionally triggers `SM-001`
(Warning), plus `SM-003` for visible keys. It is not a recommendation to use
bidirectional relationships in production. The model is not changed by `pbiq`.

## Invalid PBIP shortcut

```powershell
dotnet run --project src/PowerBIQualityGate.Cli -- scan examples/invalid-pbip-shortcut
dotnet run --project src/PowerBIQualityGate.Cli -- audit examples/invalid-pbip-shortcut
$LASTEXITCODE # expected 1; audit NOT RUN
```

The invalid shortcut intentionally triggers `PBI-002`. Error code `1` signals
structural failure before any TOM audit occurs.

For policy/CI usage and the `0`/`1`/`2` contract, see
[quality-gate CI guidance](../docs/quality-gate-ci.md).
