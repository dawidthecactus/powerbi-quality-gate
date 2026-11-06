# Contributing to Power BI Quality Gate

Thank you for helping improve `powerbi-quality-gate` (`pbiq`). The project is in early development, so small, focused, well-tested changes are especially valuable.

## Before you start

- Check existing [issues](https://github.com/dawidthecactus/powerbi-quality-gate/issues) before starting a substantial change.
- For new rules, format support, or changes to public CLI behavior, open an issue first to discuss expected behavior and edge cases.
- Keep pull requests focused: one coherent change per PR.
- Do not include company reports, private datasets, credentials, customer details, or proprietary PBIP files. Use synthetic fixtures with invented data.
- The project is published under the [MIT license](LICENSE). Review third-party rights and use only code or fixtures you may legally contribute.

## Development environment

Install the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) and Git. Then:

```powershell
git clone https://github.com/dawidthecactus/powerbi-quality-gate.git
cd powerbi-quality-gate
dotnet restore
dotnet build -c Release
```

The repository's `global.json` controls SDK selection. Formatting rules are defined in `.editorconfig`; Git line-ending rules are in `.gitattributes`.

## Project layout

- `src/PowerBIQualityGate.Core`: pure discovery, validation, and diagnostic logic. Avoid writing directly to `Console` from this layer.
- `src/PowerBIQualityGate.Cli`: command definitions, argument handling, user-facing messages, and exit codes.
- `tests/PowerBIQualityGate.Tests`: xUnit tests for successful, invalid, and ambiguous inputs.
- `docs/`: explanations of supported formats, diagnostic codes, and limitations.

Keep filesystem access read-only for scanners and validators unless a future feature explicitly requires otherwise.

## Working on a change

Create a descriptive branch from the latest `main`:

```powershell
git checkout main
git pull --ff-only
git switch -c feat/describe-change
```

Suggested branch prefixes: `feat/`, `fix/`, `docs/`, `test/`, `refactor/`, and `ci/`.

Prefer incremental changes and atomic commits. We use **Conventional Commits**:

```text
feat(discovery): support additional PBIP entry points
fix(validation): handle missing model references
test(validation): cover ambiguous report paths
docs: clarify scan exit codes
ci: improve cross-platform checks
```

Write meaningful commit messages that describe **what changed**, not just `update` or `fix stuff`.

## Quality checklist

Run all checks before opening a PR:

```powershell
dotnet restore
dotnet build -c Release -warnaserror
dotnet test -c Release
dotnet format whitespace --verify-no-changes
git diff --check
```

Please ensure that:

- New behavior is covered by tests, including representative failure and edge cases.
- Validation results are deterministic for the same project and code version.
- Diagnostics have stable identifiers and actionable messages.
- Error paths do not hide unexpected failures or silently turn errors into successes.
- CLI error cases return appropriate non-zero exit codes.
- Relevant docs are updated whenever supported behavior or limitations change.
- No generated `bin/` or `obj/` files, local secrets, or real customer data are committed.

CI runs on both Windows and Ubuntu. A local pass is important, but the PR is ready for review only when required CI checks pass.

## Tests and fixtures

Prefer synthetic projects created in isolated temporary directories. Clean them up in `finally` blocks or equivalent disposables. Test at least one positive path and one negative path when introducing a new rule.

When testing file formats, distinguish **structural fixtures** from genuine Power BI Desktop exports. A synthetic fixture passing a structure check does not prove that Desktop will open it.

If you add or change a diagnostic code, document it in [project-structure-validation.md](docs/project-structure-validation.md).

## Pull requests

In your PR description, include:

1. **Problem:** what behavior is missing or incorrect?
2. **Approach:** what changed and why?
3. **Tests:** which commands and scenarios did you run?
4. **Compatibility:** any changes to commands, diagnostics, exit codes, or supported PBIP structures?
5. **Related issue:** link it when applicable.

Keep unrelated formatting changes out of feature PRs where feasible. If a formatter modifies existing files, separate those changes into a clear formatting commit.

Do not assume maintainers will merge a PR solely because CI is green: correctness, readability, scope, and documentation matter too.

## Reporting a bug

Include the `pbiq` command, actual and expected behavior, exit code, OS, .NET SDK version, and a **sanitized or synthetic** minimal reproduction. Do not post sensitive project files in public issues.

## Code of conduct

Be respectful and constructive. Focus discussions and reviews on the code and evidence, not the person. Harassment and discriminatory behavior are not welcome.
