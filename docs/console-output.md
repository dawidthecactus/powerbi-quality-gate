# Console output

`pbiq scan` renders a compact terminal report designed for interactive use and CI logs.

## Layout

1. **PBIQ header:** version (taken from the CLI assembly), offline structural-validation scope.
2. **Project identity:** shortcut name and absolute path.
3. **Project structure:** PBIP entry point, report format (PBIR / PBIR-Legacy), and model format (TMDL / TMSL / external connection). Unknown formats display **Not resolved** and are never marked successful.
4. **Findings:** one compact block per diagnostic, showing severity, code, explanation and source path. No wide diagnostic table, even for long Windows paths.
5. **Summary:** errors, warnings and informational diagnostics counted from actual emitted findings.
6. **Final outcome:** PASS/FAIL with the unchanged exit code; scope note avoids implying complete report correctness.

## Behavior and compatibility

- `--no-color` and `NO_COLOR` disable ANSI styling.
- Redirected stdout/stderr also suppresses ANSI styling automatically.
- Successful scans write to stdout; failed discovery and failed validation write to stderr.
- Exit codes remain unchanged: `0` for successful implemented structure checks, `1` for structural validation failure, `2` for project discovery failure.
- The renderer is presentation-only and does not modify Power BI files or change diagnostics.
- It does **not** claim to lint DAX, inspect model quality, validate Microsoft JSON Schemas, or check Power BI Desktop compatibility.

## Try it

```powershell
dotnet run --project src/PowerBIQualityGate.Cli -- scan examples/valid-pbir-tmdl
$LASTEXITCODE

dotnet run --project src/PowerBIQualityGate.Cli -- scan examples/invalid-pbip-shortcut --no-color
$LASTEXITCODE
```
