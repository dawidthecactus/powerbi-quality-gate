#!/usr/bin/env python3
"""Fail on stale release-facing v0.3 docs or missing packaged-binary gates.

Static tests only. The release workflow must still build and execute the
self-contained Windows/Linux executables and an external consumer smoke test.
"""
from __future__ import annotations

import re
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent


def load(path: str) -> str:
    return (ROOT / path).read_text(encoding="utf-8")


def require(condition: bool, reason: str) -> None:
    if not condition:
        raise ValueError(reason)


def run() -> None:
    project = load("src/PowerBIQualityGate.Cli/PowerBIQualityGate.Cli.csproj")
    version = re.findall(r"<Version>([^<]+)</Version>", project)
    require(len(version) == 1, "Expected one CLI <Version> element")
    require(version[0].startswith("0.3.0-alpha."), "Readiness requires the v0.3 alpha CLI source")

    required_docs = {
        "README.md": ("0.3.0-alpha.1", "v0.2.0-rc.1", "Fabric Git", "--format sarif", "--format json", ".pbiq.yml", "GitHub Action"),
        "docs/getting-started.md": ("0.3.0-alpha.1", "v0.2.0-rc.1", "--format json", "--format sarif", ".SemanticModel"),
        "docs/commands.md": ("--format", "--config", "--sarif-source-root", "Fabric Git", "JSON"),
        "docs/ci-integration.md": ("upload-sarif@v4", "exit-code", "sarif-path", "security-events", "REPLACE_WITH_REVIEWED_FULL_COMMIT_SHA"),
        "docs/release-process.md": ("v0.3.0-alpha.1", "v0.2.0-rc.1", "dry run", "main", "SHA256"),
    }
    for file, phrases in required_docs.items():
        doc = load(file)
        for phrase in phrases:
            require(phrase in doc, f"{file}: missing {phrase}")

    readme = load("README.md")
    require("File and line locations are intentionally `null`" not in readme,
            "README still claims TMDL locations are always null")
    require("Fabric exports without `.pbip` are not" not in readme,
            "README still incorrectly rejects Fabric Git audit")
    require("All commands take a `.pbip`" not in readme,
            "README still claims every command is PBIP-only")

    smoke = load("scripts/smoke-test-release.ps1")
    for label in ("JSON baseline", "JSON warning policy", "JSON configured exclusion",
                  "Fabric SARIF baseline", "Fabric SARIF warning policy", "JSON missing model"):
        require(label in smoke, f"Packaged executable smoke lacks {label}")
    require("Start-Process -FilePath $binary" in smoke, "Must test extracted binary, not dotnet DLL")
    require("ConvertFrom-Json" in smoke and "pbiqExitCode" in smoke,
            "Machine output content/exit validation missing")
    require("(Get-Item -LiteralPath $stderr).Length" in smoke,
            "Smoke must handle empty stderr under strict PowerShell")

    release = load(".github/workflows/release.yml")
    require("./scripts/smoke-test-release.ps1" in release,
            "Release packages must run the executable smoke test")
    require("SHA256SUMS.txt" in release and "dotnet publish" in release,
            "Expected standalone packaging and checksum gate")
    require("scripts/verify-03-7-readiness.py" in release,
            "Release metadata must check documentation/source consistency")

    ci = load(".github/workflows/ci.yml")
    require("scripts/verify-03-7-readiness.py" in ci,
            "Cross-platform CI missing readiness check")
    require("scripts/test-audit-fabric.ps1" in ci and "uses: ./" in ci,
            "Fabric CLI and composite Action regressions must remain in CI")

    attrs = load(".gitattributes")
    for entry in ("*.yml text eol=lf", "*.yaml text eol=lf",
                  "*.ps1 text eol=lf", "*.py text eol=lf"):
        require(entry in attrs, f"Missing line-ending policy: {entry}")

    for fixture in ("examples/fabric-git/Revenue.SemanticModel/definition.pbism",
                    "examples/fabric-git-warning/Warning.SemanticModel/definition.pbism"):
        require((ROOT / fixture).is_file(), f"Missing public Fabric example: {fixture}")

    print("PASS: v0.3 release documentation, package smoke, CI and source consistency")


if __name__ == "__main__":
    try:
        run()
    except (ValueError, OSError) as error:
        print(f"FAIL: v0.3 readiness: {error}", file=sys.stderr)
        raise SystemExit(1)
