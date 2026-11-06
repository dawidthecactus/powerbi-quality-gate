#!/usr/bin/env python3
"""Source-level checks for the new 03.5 wrapper; CI runs real action tests."""
from pathlib import Path
import sys

root = Path(__file__).resolve().parent.parent
try:
    manifest = (root / 'action.yml').read_text(encoding='utf-8')
    runner = (root / 'scripts/run-composite-action.ps1').read_text(encoding='utf-8')
    workflow = (root / '.github/workflows/ci.yml').read_text(encoding='utf-8')
    docs = (root / 'docs/milestone-03.5-reusable-action.md').read_text(encoding='utf-8')
    required_inputs = ('project-path', 'config-path', 'fail-on-warning',
                       'min-measure-coverage', 'sarif-output')
    assert 'using: composite' in manifest
    assert 'actions/setup-dotnet@v6' in manifest
    assert all(('  ' + name + ':') in manifest for name in required_inputs)
    assert 'steps.audit.outputs.exit-code' in manifest
    assert 'steps.audit.outputs.sarif-path' in manifest
    assert 'GITHUB_WORKSPACE' in runner and 'GITHUB_OUTPUT' in runner
    assert '--sarif-source-root' in runner and '--format' in runner
    assert 'ConvertFrom-Json' in runner and 'pbiqExitCode' in runner
    assert 'uses: ./' in workflow and 'continue-on-error: true' in workflow
    assert 'Reusable PBIQ action smoke' in workflow
    assert '392' in docs
except (AssertionError, OSError) as exc:
    sys.exit(f'FAIL: PBIQ action v0.3.5 source checks: {exc}')
print('PASS: 03.5 composite action/source/CI source checks (not a substitute for action CI)')
