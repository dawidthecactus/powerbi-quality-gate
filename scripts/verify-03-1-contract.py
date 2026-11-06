#!/usr/bin/env python3
"""Static verification for distributable 03.1 sources (no .NET needed).

Does not substitute for dotnet build/test. Run after apply-03-1-json.py.
"""
import json
from pathlib import Path
import sys

ROOT = Path(__file__).resolve().parent.parent
schema = ROOT / 'docs/schemas/audit-report.v1.schema.json'
command = ROOT / 'src/PowerBIQualityGate.Cli/Commands/AuditCommand.cs'
renderer = ROOT / 'src/PowerBIQualityGate.Cli/Rendering/AuditJsonRenderer.cs'
ci = ROOT / '.github/workflows/ci.yml'

try:
    schema_obj = json.loads(schema.read_text(encoding='utf-8'))
    assert schema_obj['properties']['schemaVersion']['const'] == '1.0.0'
    text = command.read_text(encoding='utf-8')
    assert 'new Option<string?>("--format")' in text
    assert 'AuditJsonRenderer.RenderSuccess' in text
    assert 'jsonOutput: ' not in text or 'RenderSuccess' in text
    assert 'private static void RenderFailure(' in text
    assert 'new AuditReportV1(' in renderer.read_text(encoding='utf-8')
    assert 'scripts/test-audit-json.ps1' in ci.read_text(encoding='utf-8')
except (OSError, AssertionError, KeyError, json.JSONDecodeError) as error:
    print(f'FAIL: 03.1 contract check: {error}', file=sys.stderr)
    sys.exit(1)

print('PASS: 03.1 JSON source/schema/CI static checks (not .NET compilation)')
