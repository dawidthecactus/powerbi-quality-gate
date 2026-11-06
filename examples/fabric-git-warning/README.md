# Fabric Git warning fixture

No `.pbip` shortcut. The native `Warning.SemanticModel` contains one
bidirectional relationship to exercise `SM-001` and the Quality Gate policy.

```pwsh
dotnet run -c Release --project src/PowerBIQualityGate.Cli -- audit examples/fabric-git-warning --fail-on-warning --format sarif --sarif-source-root .
```

Expected CLI exit code: 1; SARIF remains valid and includes the source
location for the relationship.
