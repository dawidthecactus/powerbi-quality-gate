# Semantic Model Inspector · Milestone 02.1

The new `pbiq inspect <path>` subcommand provides a **preliminary, read-only
inventory of local TMDL declarations** for structurally valid Power BI projects.
It is separate from `pbiq scan`: the existing structural gate, diagnostic codes,
and exit codes are unchanged.

## Try it

```powershell
dotnet run --project src/PowerBIQualityGate.Cli -- inspect examples/valid-pbir-tmdl

dotnet run --project src/PowerBIQualityGate.Cli -- inspect examples/valid-pbir-tmdl --no-color
```

Expected inventory of this **synthetic** project:

- 2 tables: `Sales` and `Product`
- 5 columns
- 2 measures
- 1 hierarchy
- 1 relationship

The inventory is based on lexical declaration counting in `definition/tables/*.tmdl`
and `definition/relationships.tmdl`. It reports declared objects, not their
correctness or semantic validity. Table names are displayed; expressions are
not evaluated. The sample is a test fixture, **not a full Power BI Desktop
export**.

## Scope and limitations

- Only a local TMDL model is supported. TMSL (`model.bim`) and external live
  connections return exit code 2 (unsupported inspection type).
- An invalid PBIP structure returns 1 and points users to `pbiq scan`.
- A missing path or an unreadable / malformed table file returns 2.
- `inspect` does not verify DAX, detect broken relationships, resolve measures,
  validate TMDL grammar, or load any data. Counted declarations might not
  resolve successfully in an actual Tabular model.
- The provisional reader assumes standard TMDL formatting and one table per
  `tables/*.tmdl` file; exotic multiline expressions could cause false counts.
- The next implementation milestone should replace lexical reading with
  Microsoft's official TOM/TMDL deserializer for authoritative model objects.

Microsoft documents both the folder layout and the official serializer:

- https://learn.microsoft.com/en-us/analysis-services/tmdl/tmdl-overview
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.analysisservices.tabular.tmdlserializer

## Exit codes

| Code | Meaning |
| --- | --- |
| 0 | An inventory was produced; **not** a guarantee of a valid model. |
| 1 | PBIP structural validation failed. |
| 2 | Project discovery, unsupported model format, or reading failed. |
