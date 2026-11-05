#!/usr/bin/env python3
"""Fast, dependency-free VHS tape preflight for PBIQ screenshot workflow.

This catches common malformed /regex/ delimiters before the slower
Dotnet/VHS installation. The official `vhs validate` is still required.
"""

from __future__ import annotations

import argparse
from pathlib import Path
import re
import sys

EXPECTED = {
    "scan.tape": "scan-color.png",
    "inspect-measures.tape": "inspect-measures.png",
    "audit-summary.tape": "audit-summary-color.png",
    "audit-warning.tape": "audit-warning-color.png",
}

# An inner slash must be escaped (\/); VHS uses /.../ as delimiters.
WAIT = re.compile(
    r"^Wait(?:\+(?:Screen|Line))?(?:@\d+(?:ms|s|m))?\s+/(?:\\.|[^/\\])*/$"
)


def check_tape(path: Path, screenshot: str) -> list[str]:
    errors: list[str] = []
    if not path.is_file():
        return [f"Missing tape: {path.name}"]
    text = path.read_text(encoding="utf-8")
    waits = 0
    for index, raw_line in enumerate(text.splitlines(), 1):
        line = raw_line.strip()
        if line.startswith("Wait") and not line.startswith("#"):
            waits += 1
            if not WAIT.fullmatch(line):
                errors.append(
                    f"{path.name}:{index}: invalid VHS Wait regex delimiters: {line}"
                )
    if waits == 0:
        errors.append(f"{path.name}: no Wait command")
    screenshot_lines = [
        ln.strip() for ln in text.splitlines() if ln.strip().startswith("Screenshot ")
    ]
    if screenshot_lines != [f"Screenshot assets/screenshots/{screenshot}"]:
        errors.append(f"{path.name}: expected one Screenshot for {screenshot}")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path, help="Path to assets/vhs")
    args = parser.parse_args()
    errors = [
        error
        for filename, screenshot in EXPECTED.items()
        for error in check_tape(args.directory / filename, screenshot)
    ]
    if errors:
        for error in errors:
            print(f"FAIL: {error}", file=sys.stderr)
        return 1
    print(f"PASS: {len(EXPECTED)} VHS tapes passed static preflight")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
