#!/usr/bin/env python3
"""Fail fast on stale PBIQ public docs and broken local README media links.

Pure stdlib, no network requests, no OCR, no access to private PBIP projects.
Run from any working directory; checks the checkout containing this script.
"""
from __future__ import annotations

from pathlib import Path
import re
import struct
import sys
from urllib.parse import unquote, urlsplit

ROOT = Path(__file__).resolve().parent.parent
README = ROOT / "README.md"
CSPROJ = ROOT / "src/PowerBIQualityGate.Cli/PowerBIQualityGate.Cli.csproj"
IMAGES = (
    "assets/screenshots/scan-color.png",
    "assets/screenshots/inspect-measures.png",
    "assets/screenshots/audit-summary-color.png",
    "assets/screenshots/audit-warning-color.png",
)


def check() -> None:
    readme = README.read_text(encoding="utf-8")
    project = CSPROJ.read_text(encoding="utf-8")
    versions = re.findall(r"<Version>([^<]+)</Version>", project)
    if len(versions) != 1:
        raise ValueError("Expected exactly one explicit CLI <Version> element")
    version = versions[0]
    if not re.fullmatch(r"\d+\.\d+\.\d+(?:-[0-9A-Za-z][0-9A-Za-z.-]*)?", version):
        raise ValueError(f"Invalid project SemVer: {version}")
    if version not in readme:
        raise ValueError(f"README must mention current CLI version {version}")
    if "<!-- MEDIA SLOT" in readme:
        raise ValueError("README still contains the unpublished screenshot placeholder")

    # Match Markdown links/images; ignore remote links and in-page anchors.
    markdown_links = re.findall(r"!?\[[^\]]*\]\(([^)]+)\)", readme)
    for link in markdown_links:
        # URL with optional fragment, excluding external schemes.
        uri = urlsplit(link)
        if uri.scheme or uri.netloc or not uri.path:
            continue
        relative = Path(unquote(uri.path))
        if relative.is_absolute() or ".." in relative.parts:
            raise ValueError(f"Unsafe README relative link: {link}")
        target = ROOT / relative
        if not target.is_file():
            raise ValueError(f"Broken README link: {link}")

    for path in IMAGES:
        markdown_path = f"]({path})"
        if readme.count(markdown_path) != 1:
            raise ValueError(f"Expected exactly one README image link: {path}")
        file = ROOT / path
        if not file.is_file() or file.stat().st_size < 20_000:
            raise ValueError(f"Screenshot missing or suspiciously small: {path}")
        with file.open("rb") as stream:
            header = stream.read(24)
        if len(header) != 24 or header[:8] != b"\x89PNG\r\n\x1a\n" or header[12:16] != b"IHDR":
            raise ValueError(f"Invalid PNG header: {path}")
        width, height = struct.unpack(">II", header[16:24])
        if width < 400 or height < 150:
            raise ValueError(f"Suspicious screenshot dimensions ({width}x{height}): {path}")
    # The published alpha must have a runnable quick start and reviewable notes.
    quick_start = ROOT / "docs/quick-start.md"
    release_notes = ROOT / f"docs/releases/v{version}.md"
    if not quick_start.is_file() or not release_notes.is_file():
        raise ValueError("Missing current-version Quick Start or release notes")
    quick = quick_start.read_text(encoding="utf-8")
    notes = release_notes.read_text(encoding="utf-8")
    for required in (version, "pbiq-win-x64.zip", "pbiq-linux-x64.tar.gz",
                     "SHA256SUMS.txt", "fabric-git/Revenue.SemanticModel",
                     "git clone", "Get-FileHash", "sha256sum -c",
                     "--format json", "--format sarif", "SM-001", "SM-004"):
        if required not in quick:
            raise ValueError(f"Quick Start missing current feature/example: {required}")
    for required in (version, "406/406", "SARIF", "Fabric Git", "SM-004",
                     "pbiq-win-x64.zip", "pbiq-linux-x64.tar.gz"):
        if required not in notes:
            raise ValueError(f"Release notes missing: {required}")
    for doc_name, contents in (("README", readme), ("Getting Started",
                          (ROOT / "docs/getting-started.md").read_text(encoding="utf-8"))):
        if "docs/quick-start.md" not in contents and "quick-start.md" not in contents:
            raise ValueError(f"{doc_name} does not link to Quick Start")
    for link in re.findall(r"!?\[[^\]]*\]\(([^)]+)\)", quick):
        uri = urlsplit(link)
        if uri.scheme or uri.netloc or not uri.path:
            continue
        relative = Path(unquote(uri.path))
        if relative.is_absolute() or ".." in relative.parts:
            raise ValueError(f"Unsafe Quick Start relative link: {link}")
        if not (quick_start.parent / relative).is_file():
            raise ValueError(f"Broken Quick Start local link: {link}")
    if "![Public RC]" in readme or "when one becomes available" in readme:
        raise ValueError("README still presents the published alpha as unreleased")
    print(f"PASS: README links, four PNGs, Quick Start, release notes and CLI {version}")


def main() -> int:
    try:
        check()
    except (ValueError, OSError) as exc:
        print(f"FAIL public docs: {exc}", file=sys.stderr)
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
