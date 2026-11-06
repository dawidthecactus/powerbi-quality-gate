#!/usr/bin/env python3
"""Dependency-free structural QA for public PBIQ terminal screenshots.

This checks PNG headers, sizes and dimensions only. Human visual review is
required: it cannot prove the screenshot is complete, colorful or legible.
"""
from __future__ import annotations

import argparse
from pathlib import Path
import struct
import sys

FILES = {
    "scan-color.png": (1500, 1420),
    "inspect-measures.png": (2040, 510),
    "audit-summary-color.png": (1530, 830),
    "audit-warning-color.png": (1530, 830),
}
PNG_SIGNATURE = b"\x89PNG\r\n\x1a\n"


def validate_file(path: Path, expected_dimensions: tuple[int, int]) -> None:
    if not path.is_file():
        raise ValueError(f"Missing media file: {path.name}")
    size = path.stat().st_size
    if not 25_000 <= size <= 10_000_000:
        raise ValueError(f"Suspicious PNG size for {path.name}: {size} bytes")

    with path.open("rb") as source:
        header = source.read(24)
    if len(header) != 24 or not header.startswith(PNG_SIGNATURE) or header[12:16] != b"IHDR":
        raise ValueError(f"Invalid PNG header: {path.name}")
    dimensions = struct.unpack(">II", header[16:24])
    if any(not (0.90 * expected <= actual <= 1.10 * expected)
           for actual, expected in zip(dimensions, expected_dimensions)):
        raise ValueError(
            f"Unexpected dimensions for {path.name}: {dimensions}; expected near {expected_dimensions}"
        )
    print(f"PASS {path.name}: {dimensions[0]}x{dimensions[1]} | {size:,} bytes")


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("directory", type=Path, help="Folder containing four real PNG files")
    args = parser.parse_args()
    try:
        for filename, dimensions in FILES.items():
            validate_file(args.directory / filename, dimensions)
    except ValueError as exc:
        print(f"FAIL: {exc}", file=sys.stderr)
        return 1
    print("Media file checks passed. Visual inspection is still required.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
