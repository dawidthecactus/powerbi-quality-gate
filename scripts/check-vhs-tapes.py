#!/usr/bin/env python3
"""Validate public PBIQ VHS screenshot tapes before expensive CI steps.

VHS v0.12.1 Wait+Screen can time out when the CLI scrolls the terminal.
For static screenshots, preflight requires a post-command render settle;
workflow independently verifies actual output and exit codes before capture.
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
WAIT = re.compile(
    r"^Wait(?:\+(?:Screen|Line))?(?:@\d+(?:ms|s|m))?\s+/(?:\\.|[^/\\])*/$"
)
SLEEP = re.compile(r"^Sleep\s+(\d+)(ms|s)$")


def check_tape(path: Path, screenshot: str) -> list[str]:
    if not path.is_file():
        return [f"Missing tape: {path.name}"]
    lines = [(i, line.strip()) for i, line in enumerate(
        path.read_text(encoding="utf-8").splitlines(), 1
    ) if line.strip() and not line.lstrip().startswith("#")]
    errors: list[str] = []
    screenshots = [(i, text) for i, text in lines if text.startswith("Screenshot ")]
    if len(screenshots) != 1 or screenshots[0][1] != f"Screenshot assets/screenshots/{screenshot}":
        errors.append(f"{path.name}: expected exactly one screenshot for {screenshot}")
        return errors
    screenshot_line = screenshots[0][0]
    enters = [i for i, text in lines if text == "Enter" and i < screenshot_line]
    if not enters:
        errors.append(f"{path.name}: no command Enter before screenshot")
        return errors
    command_enter = max(enters)
    for i, text in lines:
        if text.startswith("Wait"):
            if not WAIT.fullmatch(text):
                errors.append(f"{path.name}:{i}: malformed Wait regex")
            else:
                errors.append(f"{path.name}:{i}: avoid Wait in static captures; verify CLI separately and Sleep")
    settles = []
    for i, text in lines:
        match = SLEEP.fullmatch(text)
        if match and command_enter < i < screenshot_line:
            milliseconds = int(match.group(1)) * (1000 if match.group(2) == "s" else 1)
            if 2000 <= milliseconds <= 10000:
                settles.append(i)
    if not settles:
        errors.append(f"{path.name}: require 2s..10s Sleep after command Enter and before Screenshot")
    if not any(text.startswith('Type "pbiq ') for _, text in lines):
        errors.append(f"{path.name}: no visible real pbiq command")
    return errors


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path)
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
