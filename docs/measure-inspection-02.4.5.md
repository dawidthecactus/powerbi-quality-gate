# Milestone 02.4.5.1 + 02.4.5.2: Measure metadata inventory and classification

## Purpose

The current `SM-002` check can examine formatting only for visible measures
whose offline TOM data type is recognized as numeric. In a large real-world
PBIP, **763 measures returned `Unknown`**, resulting in zero numeric-type
classification coverage. That does **not** establish whether those measures
have missing, static or dynamic format metadata.

This milestone introduces a separate, read-only inventory for **every** measure,
without attempting to infer a result type from DAX expressions. It does not
change existing `SM-002` findings, warning severities, the `audit` command,
coverage-policy denominators, exit codes, or the minimalist audit UI.

## Usage

```powershell
dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- inspect examples/valid-pbir-tmdl --measures --no-color

# Inspect a private PBIP model without revealing measure names or DAX:
$model = 'C:\Reports\MyModel.pbip'
dotnet run --no-build -c Release --project src/PowerBIQualityGate.Cli -- inspect $model --measures
$LASTEXITCODE
```

`--measures` is a new **aggregate-only** output mode for `pbiq inspect`.
`--tom` and `--measures` cannot be used together (exit code `2`). Existing
`inspect` and `inspect --tom` remain unchanged. The `--measures` view is plain
text, including with `--no-color` or redirected output.

## Independent classifications

Each `TomMeasureSnapshot` is projected into an `InspectedMeasure`:

- Format metadata: `Dynamic` when `FormatStringDefinition` exists, else `Static`
  when `FormatString` is non-blank, otherwise `Missing`.
- Type metadata: `Numeric` (`Int64`, `Decimal`, `Double`), `NonNumeric`
  (`String`, `Boolean`, `DateTime`, `Binary`), `Unknown` (`Unknown`, `Variant` or
  empty), or `Unsupported` (an unrecognized value).
- Visibility: both parent table and measure must be visible. Hidden measures
  still belong to the full inventory but are not included in the visible
  classification totals.
- Edge-case flags: whitespace-only static formats, blank/unavailable dynamic
  expressions, and simultaneous static/dynamic definitions. A TOM `null` or
  empty static format cannot reliably distinguish a missing property from an
  explicitly empty value, so it is counted as `Missing` rather than assumed
  to be an invalid explicit format.

When a dynamic definition is present, `Dynamic` takes precedence over any
static fallback; the overlap is counted separately. `Unavailable` for the
expression means that a legacy or synthetic snapshot records the dynamic
presence without exposing its expression. A blank dynamic expression remains
in the `Dynamic` category but is counted as a separate data-quality flag; it
is **not** validated DAX.

The report guarantees these arithmetic invariants for visible measures:

```
Visible = StaticFormats + DynamicFormats + MissingFormats
Visible = NumericTypes + NonNumericTypes + UnknownTypes + UnsupportedTypes
Total   = Visible + Hidden
```

**Inspected** means the *presence* of formatting metadata was examined, not
that formatting is correct or that the DAX expression would return a number.
The TOM serializer reads files offline and PBIQ never saves the source project.

## Architecture

- `TomModelReader` reads `Measure.FormatString` and
  `Measure.FormatStringDefinition?.Expression` from Microsoft TOM.
- `TomMeasureSnapshot` adds one optional trailing
  `DynamicFormatStringExpression` field without breaking existing callers.
- `MeasureInspectionEngine` is an independent projection over the TOM snapshot.
  It stores measure names for future opt-in diagnostics, but does **not** copy
  DAX expressions or format text into the inspection entries.
- `MeasureInspectionRenderer` emits only aggregates, not individual names or
  DAX. The PBIP filename is shown locally for orientation.

## What to verify in a large private model

We expect `Measures inspected: 763/763` if all 763 measures are readable.
The number of `Static`, `Dynamic`, `Missing` formats is **unknown** until the
local test runs. Type metadata can still report `unknown 763`, independently.
That is a successful inspection and **not** a successful SM-002 numeric audit.

Compare with existing `pbiq audit ... --no-color` and confirm that default
verdicts, information counts and strict coverage-policy exit codes have not
changed.

## Tests, privacy, next stage

Unit tests cover every classification, blank and unavailable definitions,
visibility, a synthetic set of 763 measures, read-only behavior, renderer
privacy, real TOM deserialization of an isolated synthetic example, and
preservation of the `SM-002` warning and coverage policy. CI additionally
runs `inspect --measures` on a synthetic fixture under Windows and Linux.

**02.4.5.3** will separately decide how to use this inventory in the SM-002
rule, including which missing formats are conclusive warnings versus
information-only manual reviews. **02.4.5.4** will present both metadata
inspection coverage and numeric type classification coverage, with explicitly
different labels and no silent policy change.

Sources: Microsoft TMDL overview and TOM Measure/FormatStringDefinition API:
- https://learn.microsoft.com/en-us/analysis-services/tmdl/tmdl-overview
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.analysisservices.tabular.measure
- https://learn.microsoft.com/en-us/dotnet/api/microsoft.analysisservices.tabular.formatstringdefinition
