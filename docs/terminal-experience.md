# PBIQ Terminal Experience (Milestone 01.7.3)

The default `pbiq scan` command presents a branded **static scan report**.
The display runs after the scan has completed. It does **not** pretend to animate
checks that take only milliseconds.

## Output sections

1. **PBIQ introduction**: tool name, assembly version, local/read-only scope.
2. **Project profile**: PBIP entry point, path, report and model formats.
3. **Scan checkpoints**: discovered entry point, identified report format and
   identified model format. The words *detected* and *not resolved* are used
   deliberately. Format detection is not full validation.
4. **Diagnostic report**: severity, code, original message, relevant file,
   explanation of impact and a suggested manual fix.
5. **Final result**: error/warning/info counts and the unchanged exit code.

## Why / how-to-fix explanations

`DiagnosticGuidance.cs` contains a separate entry for each implemented diagnostic
code, `PBI-001` through `PBI-018`. Guidance is maintained in the CLI
presentation layer, not the project validator. Unknown codes receive a neutral
fallback rather than a fabricated repair instruction.

The guidance is not an automated fix and cannot guarantee that a PBIP file will
open in Power BI Desktop. Some invalid manifests should be restored from a
known-good export rather than hand-edited.

## Automation safety

- Exit code `0`: all implemented structural checks passed.
- Exit code `1`: PBIP discovery passed, but structural errors were found.
- Exit code `2`: the requested PBIP entry point could not be discovered.
- `--no-color` disables ANSI control sequences.
- Setting `NO_COLOR` also disables colors.
- Redirected output disables colors; no animations or screen clearing occurs.
- The CLI preserves the existing convention of reporting errors to stderr.

The first release does **not** implement DAX analysis, semantic model quality
rules, full Microsoft schema validation or automated repair.

## Local verification

```powershell
# This is the new renderer's unmistakable signature.
Select-String -Path src/PowerBIQualityGate.Cli/Rendering/ConsoleReportRenderer.cs -Pattern 'PROJECT PROFILE|SCAN CHECKPOINTS|WHY IT MATTERS|HOW TO FIX'

dotnet restore
dotnet build -c Release -warnaserror
dotnet test -c Release
dotnet format whitespace --verify-no-changes

dotnet run --project src/PowerBIQualityGate.Cli -- scan examples/valid-pbir-tmdl
dotnet run --project src/PowerBIQualityGate.Cli -- scan examples/invalid-pbip-shortcut
```

Never commit until the tests and visual output have been checked on the target
terminal. Keep changes to the renderer/guidance separate from validator logic.
