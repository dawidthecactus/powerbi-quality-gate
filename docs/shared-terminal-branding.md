# Milestone 02.1.1 · Shared terminal branding

This patch unifies the CLI presentation before the 02.1 commit:

- `scan` and `inspect` use the **same** large PBIQ logo, color palette, version banner, and numbered section headings.
- The shared entry point is `PbiqConsoleChrome`; future commands should reuse it rather than copy the six logo lines.
- `inspect` now shows project profile, model snapshot, a table inventory, and a clearly scoped outcome card.
- The existing structural validation, TMDL declaration counting, exit codes, `--no-color` behavior, and CI release workflow remain unchanged.
- Five new renderer tests cover sections, scope disclaimer, empty inventory, escaping, and ANSI-free output.

Important: the `inspect` success status means inventory successfully rendered, **not** that model semantics or DAX were validated.

Run:

```powershell
dotnet format whitespace
dotnet build -c Release -warnaserror
dotnet test -c Release
dotnet format whitespace --verify-no-changes
git diff --check

dotnet run --project src/PowerBIQualityGate.Cli -- inspect examples/valid-pbir-tmdl
dotnet run --project src/PowerBIQualityGate.Cli -- scan examples/valid-pbir-tmdl
```
