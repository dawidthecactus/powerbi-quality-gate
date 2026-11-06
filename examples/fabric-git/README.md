# Fabric Git semantic model fixture

This example intentionally has **no `.pbip` shortcut or `.Report` folder**.
It is a Git-style item directory with `Revenue.SemanticModel/definition.pbism`
and `Revenue.SemanticModel/definition/*.tmdl`.

From the repository root:

```pwsh
dotnet run -c Release --project src/PowerBIQualityGate.Cli -- audit examples/fabric-git --format json

dotnet run -c Release --project src/PowerBIQualityGate.Cli -- audit examples/fabric-git/Revenue.SemanticModel --format sarif --sarif-source-root .
```

The fixture uses synthetic data model metadata and the same tiny, public
TMDL declarations as the existing PBIP regression fixture.
