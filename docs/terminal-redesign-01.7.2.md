# CLI redesign, 01.7.2

`pbiq scan` is a one-shot structural validator, not an interactive terminal dashboard.

## Visual language

- Short PBIQ wordmark and version line without a decorative header panel.
- Structural checks in three consistently aligned rows.
- Brief findings rather than a full-width diagnostics table.
- Explicit error/warning/info counts and clear PASS/FAIL status.
- Read-only scope disclaimer so a PASS does not imply full model validation.

## Safety and automation

- `0`: structural checks passed; `1`: structural validation failed; `2`: project discovery failed.
- The `--no-color` flag is unchanged. Redirected output disables ANSI colors.
- Arbitrary project paths and diagnostic messages are escaped before Spectre markup rendering.
- No new validation rules or data accesses were added by this patch.
- The Unicode line separators and check marks are intended for modern terminals such as Windows Terminal.

## Verification after extracting

On Windows PowerShell, confirm the new source file actually replaced the old renderer:

```powershell
Select-String -Path .\src\PowerBIQualityGate.Cli\Rendering\ConsoleReportRenderer.cs -Pattern 'PROJECT STRUCTURE|Issue / location|WriteFooter'
```

Expected: `PROJECT STRUCTURE` and `WriteFooter` found, `Issue / location` absent.
