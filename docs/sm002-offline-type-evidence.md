# SM-002.1: offline measure type evidence

## Question

When PBIQ loads native TMDL from the filesystem, does the official Microsoft TOM deserializer populate `Measure.DataType` for DAX measures, or does PBIQ lose an existing type during projection?

The current implementation calls `TmdlSerializer.DeserializeDatabaseFromFolder()` and projects `measure.DataType.ToString()` into an immutable `TomMeasureSnapshot`. The SM-002 warning requires **known numeric type** and **missing format metadata**. Missing formats with unknown types remain nonblocking review items.

## Real-model pilot observation (anonymous aggregates only)

Five project representations from the private pilot were audited successfully. Their diagnostic signatures form two groups; therefore they should not be treated as five independent models.

| Representative | Visible measures | Static formats | Dynamic formats | Missing formats | TOM unknown type |
| --- | ---: | ---: | ---: | ---: | ---: |
| P01 | 120 | 53 | 0 | 67 | 120 |
| P02 | 763 | 104 | 0 | 659 | 763 |

All real-model finding classifications are **uncertain** pending contextual review; neither 67 nor 659 is a confirmed SM-002 violation. Never commit customer model paths, DAX, reviewer CSVs or raw PBIQ outputs.

## Evidence tests

Run on the branch with the new test file:

```powershell
dotnet test -c Release --filter 'FullyQualifiedName~Sm002OfflineTypeEvidenceTests'
dotnet test -c Release
python -m unittest discover -s tests/pilot -p 'test_*.py' -v
```

The tests use only the repository's public synthetic `examples/valid-pbir-tmdl` fixture:

1. Compare the official TOM `Measure.DataType` for `SUM(...)` and `DISTINCTCOUNT(...)` with the value in PBIQ's snapshot. The referenced decimal column is known; the measure result may still be `Unknown`.
2. Add a static format to a temporary **copy** of the synthetic TMDL. Verify that presence of a format is independent of knowing the measure's result type, and that the original fixture remains unchanged.
3. Check the current conservative rule contract: unknown type + missing format is a review item, not a confirmed `SM-002` Warning.

These regression tests deliberately assert the observed offline behavior of the pinned Microsoft.AnalysisServices dependency. If Microsoft changes it in a future version, investigate the failing assertion before modifying the rule.

## Decision gate

- If the **raw Microsoft TOM value is Unknown** and the PBIQ snapshot is Unknown, this is an upstream offline classification limitation, not a projection bug.
- If **raw TOM has a known type** but the snapshot does not, fix the projection and add a regression test.
- If a static format changes the apparent type, investigate the inconsistency before enabling any warnings.
- Never promote `Unknown` to numeric solely because a measure is missing a format string; no DAX evaluation or guessing from identifier names.

## Follow-up candidates (not implemented here)

- SM-002.2: explicitly label known TOM type vs unclassified offline measure. Consider a carefully scoped **optional** static DAX inference only with documented confidence and strong counterexamples; do not silently change strict gate warnings.
- SM-002.3: separate user-facing format-presence inventory from confirmed formatting defects. Preserve existing JSON v1 schema and release behavior until a reviewed contract change.
- Validate against synthetics and (locally only) the private pilot. Do **not** move or rebuild the immutable `v0.3.0-alpha.1` tag.

## Public references

- Microsoft Learn: [TMDL serializer API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.analysisservices.tabular.tmdlserializer)
- Microsoft Learn: [TMDL overview](https://learn.microsoft.com/en-us/analysis-services/tmdl/tmdl-overview)
