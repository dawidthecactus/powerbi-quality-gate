#!/usr/bin/env python3
"""Static 03.6 source checks. Does not replace dotnet build, unit tests or action CI."""
from pathlib import Path
import json

root = Path(__file__).resolve().parent.parent

def read(path: str) -> str:
    return (root / path).read_text(encoding='utf-8')

assert 'class AuditInputDiscovery' in read('src/PowerBIQualityGate.Core/Discovery/AuditInputDiscovery.cs')
assert 'new AuditInputDiscovery().Discover(path)' in read('src/PowerBIQualityGate.Cli/Commands/AuditCommand.cs')
assert 'ValidateFabricSemanticModel' in read('src/PowerBIQualityGate.Core/Validation/ProjectStructureValidator.cs')
assert 'Fabric Git semantic model audit smoke' in read('.github/workflows/ci.yml')
assert 'Reusable PBIQ action smoke (Fabric Git item)' in read('.github/workflows/ci.yml')
assert 'single .SemanticModel' in read('action.yml')
assert not list((root / 'examples/fabric-git').rglob('*.pbip'))
assert not list((root / 'examples/fabric-git-warning').rglob('*.pbip'))
for folder in ('examples/fabric-git/Revenue.SemanticModel', 'examples/fabric-git-warning/Warning.SemanticModel'):
    assert json.loads(read(folder + '/definition.pbism'))['version'] == '4.0'
    for part in ('model.tmdl', 'database.tmdl'):
        assert (root / folder / 'definition' / part).is_file()
    assert list((root / folder / 'definition/tables').glob('*.tmdl'))
print('PASS: 03.6 Fabric Git fixture and source checks (not .NET compilation)')
