# Milestone 02.3.3: Branded semantic-model audit

## User experience

`pbiq audit <path>` is an offline, read-only evaluation of a local TMDL
semantic model. The command reuses the shared `PbiqConsoleChrome` header
and runs four rules (`SM-001` through `SM-004`) from
`ModelQualityRulePack.CreateDefault()`.

```powershell
dotnet run --project src/PowerBIQualityGate.Cli -- audit examples/valid-pbir-tmdl
$LASTEXITCODE

dotnet run --project src/PowerBIQualityGate.Cli -- audit examples/valid-pbir-tmdl --no-color
```

The terminal report has four sections:

1. **PROJECT PROFILE:** PBIP entry point, TOM model name, compatibility level.
2. **RULE COVERAGE:** number of rules evaluated, codes and issue counts.
3. **DIAGNOSTIC REPORT:** the actual advisory findings with `WHY IT MATTERS`
   and `HOW TO FIX`, plus affected model-object locations.
4. **FINAL RESULT:** PASS / PASS WITH ADVISORIES / FAIL / NOT EVALUATED.

Up to 30 finding details are printed. Summary counts include **every**
finding even when the displayed list is truncated. Untrusted model metadata
is escaped before being rendered by Spectre.Console.

## Pipeline and exit codes

1. Discover one `.pbip` project entry point.
2. Validate structural PBIP requirements using the existing validator.
3. Check for a local TMDL model.
4. Deserialize the TMDL folder once with the official Microsoft TOM parser.
5. Run `ModelQualityAuditEngine` with the default four rules.
6. Present results using the shared PBIQ terminal identity.

| Code | Meaning |
| --- | --- |
| `0` | Quality rules executed; no **Error** findings. Warnings/info may remain. |
| `1` | Structural validation failed or the quality audit found **Error** findings. |
| `2` | Discovery failed, format unsupported, TOM load failed, rule execution failed, or no rules were evaluated. |

Outputs with exit code `0` go to stdout. Failures go to stderr. Colors can be
disabled with `--no-color` or the `NO_COLOR` environment variable, and are
automatically disabled when stdout/stderr is redirected.

This is intentionally a **metadata-only advisory quality gate**. In this
milestone the shipped rules have Warning and Information severity, so a
successful four-rule audit normally exits `0`, even if it recommends
improvements. The rule engine already supports Error severity; configuring
blocking policies and broader integration tests is Milestone 02.3.4.

A report of `PASS` never means the model is universally correct. The tool
does not evaluate DAX, inspect data values, refresh a model, connect to
Fabric, or modify source files. A failure to deserialize TMDL or evaluate
a rule is never reported as a successful audit.

## Manual verification

```powershell
dotnet restore
dotnet build -c Release -warnaserror
dotnet test -c Release
dotnet format whitespace --verify-no-changes
git diff --check

dotnet run --project src/PowerBIQualityGate.Cli -- audit examples/valid-pbir-tmdl
$LASTEXITCODE  # expected: 0 with informational SM-003 findings

dotnet run --project src/PowerBIQualityGate.Cli -- audit examples/invalid-pbip-shortcut
$LASTEXITCODE  # expected: 1, no quality audit executed
```

The existing `pbiq scan` and `pbiq inspect` commands are unchanged.

## Next milestone

02.3.4 will focus on configurable failure policy, exit-code regression tests,
real PBIP/TMDL export testing, and cross-platform CI hardening.
