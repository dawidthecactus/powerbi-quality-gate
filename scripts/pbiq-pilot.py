#!/usr/bin/env python3
"""Private, reproducible pilot harness for published PBIQ v0.3 alpha.

Never copies source projects, and never uploads pilot data. Outputs containing
model identifiers are written only to a user-selected non-Git directory.
"""
from __future__ import annotations

import argparse
import csv
import datetime as dt
import hashlib
import json
import pathlib
import re
import subprocess
import sys
import time
from collections import Counter

EXPECTED_PREFIX = "0.3.0-alpha.1"
CLASSES = ("true_positive", "false_positive", "intentional", "uncertain")
CSV_FIELDS = ("case_id", "finding_id", "rule_id", "severity", "object_path", "file_path", "line", "message", "classification", "notes")
RUN_SCHEMA = "pbiq-real-pilot-v1"


def inside_git(path: pathlib.Path) -> bool:
    """Conservatively detect Git working trees, including nested worktrees."""
    path = path.resolve()
    for parent in (path, *path.parents):
        if (parent.joinpath(".git").exists()):
            return True
    return False


def load_cases(manifest: pathlib.Path) -> list[dict[str, str]]:
    with manifest.open("r", encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream)
        if reader.fieldnames != ["case_id", "kind", "path"]:
            raise ValueError("Manifest header must be exactly: case_id,kind,path")
        rows = list(reader)
    if not 3 <= len(rows) <= 5:
        raise ValueError("Pilot must have 3 to 5 real models.")
    for index, row in enumerate(rows, start=1):
        if set(row) != {"case_id", "kind", "path"}:
            raise ValueError("Unexpected or missing CSV fields.")
        if row["case_id"] != f"P{index:02d}":
            raise ValueError("Case IDs must be sequential P01..P05 (no business names).")
        if row["kind"] not in ("pbip", "fabric"):
            raise ValueError("kind must be pbip or fabric.")
        candidate = pathlib.Path(row["path"]).expanduser()
        if not candidate.is_absolute():
            candidate = manifest.parent / candidate
        candidate = candidate.resolve()
        if not candidate.exists():
            raise ValueError(f"Model path does not exist for {row['case_id']}.")
        if row["kind"] == "fabric" and not candidate.is_dir():
            raise ValueError(f"Fabric input must be a directory ({row['case_id']}).")
        if row["kind"] == "pbip" and candidate.is_file() and candidate.suffix.lower() != ".pbip":
            raise ValueError(f"PBIP input must be a .pbip or directory ({row['case_id']}).")
        row["path"] = str(candidate)
    return rows


def execute(binary: pathlib.Path, args: list[str], timeout: int) -> dict:
    started = time.perf_counter()
    try:
        run = subprocess.run(
            [str(binary), *args], capture_output=True, text=True,
            encoding="utf-8", errors="replace", timeout=timeout, check=False,
        )
        return {"exit": run.returncode, "stdout": run.stdout, "stderr": run.stderr,
                "elapsed_seconds": round(time.perf_counter() - started, 3)}
    except subprocess.TimeoutExpired:
        return {"exit": None, "stdout": "", "stderr": "Process timed out.",
                "elapsed_seconds": round(time.perf_counter() - started, 3)}
    except OSError:
        return {"exit": None, "stdout": "", "stderr": "Process could not be launched.",
                "elapsed_seconds": round(time.perf_counter() - started, 3)}


def signature(document: dict) -> str:
    """Compare content, not paths or timestamp, for a repeat-run stability check."""
    diagnostics = [
        (item.get("ruleId"), item.get("severity"), item.get("message"),
         item.get("location", {}).get("objectPath"))
        for item in document.get("diagnostics", [])
    ]
    payload = {
        "result": document.get("result"),
        "summary": document.get("summary"),
        "coverage": document.get("coverage"),
        "rules": document.get("rules"),
        "diagnostics": sorted(diagnostics, key=lambda item: str(item)),
        "failure": document.get("failure"),
    }
    return hashlib.sha256(json.dumps(payload, ensure_ascii=False, sort_keys=True).encode("utf-8")).hexdigest()


def finding_id(case_id: str, finding: dict) -> str:
    payload = "\x1f".join((case_id, str(finding.get("ruleId", "")),
                           str(finding.get("location", {}).get("objectPath", "")),
                           str(finding.get("message", ""))))
    return hashlib.sha256(payload.encode("utf-8")).hexdigest()[:20]


def safe_spreadsheet(value: str) -> str:
    """Prevent CSV formula execution when private reviews are opened in Excel."""
    value = str(value).replace("\x00", "")
    return "\u0027" + value if value.lstrip().startswith(("=", "+", "-", "@")) else value

def safe_coverage(document: dict) -> dict:
    info = document.get("coverage")
    if not isinstance(info, dict):
        return {}
    measures = info.get("measures")
    if not isinstance(measures, dict):
        return {}
    return {key: measures.get(key) for key in
            ("inspected", "visible", "checkedNumeric", "candidates", "unknownType",
             "needsReview", "classificationPercent", "hasGaps")}


def summarize_case(case: dict, data: dict | None, audit_exit: int | None,
                   scan_exit: int | None, inspect_exit: int | None,
                   repeat_matches: bool | None, elapsed_seconds: float | None = None) -> dict:
    result = data.get("result", {}) if data else {}
    failure = data.get("failure") if data else None
    evaluated = bool(data and not failure and result.get("status") != "not_evaluated"
                     and result.get("exitCode") == audit_exit and audit_exit in (0, 1))
    findings = data.get("diagnostics", []) if data else []
    return {
        "case_id": case["case_id"], "kind": case["kind"],
        "scan_exit_code": scan_exit, "inspect_exit_code": inspect_exit,
        "audit_exit_code": audit_exit, "evaluated": evaluated,
        "audit_seconds": elapsed_seconds,
        "model_size": {key: (data.get("project") or {}).get(key) for key in
                       ("tables", "measures", "relationships")} if data else {},
        "audit_status": result.get("status") if data else "unparseable",
        "failure_code": failure.get("code") if isinstance(failure, dict) else None,
        "summary": {key: (data.get("summary") or {}).get(key, 0) for key in
                    ("rulesEvaluated", "errors", "warnings", "information", "suppressed")} if data else {},
        "findings_by_rule": dict(sorted(Counter(f.get("ruleId", "unknown") for f in findings).items())),
        "coverage": safe_coverage(data or {}),
        "repeat_consistent": repeat_matches,
    }


def verify_private_destination(path: pathlib.Path, label: str) -> None:
    if inside_git(path):
        raise ValueError(f"{label} is inside a Git worktree. Move it outside all repositories.")


def pilot_run(args: argparse.Namespace) -> int:
    manifest = pathlib.Path(args.manifest).expanduser().resolve()
    destination = pathlib.Path(args.out).expanduser().resolve()
    binary = pathlib.Path(args.pbiq).expanduser().resolve()
    if not manifest.is_file():
        raise ValueError("Manifest file does not exist.")
    if not binary.is_file():
        raise ValueError("PBIQ executable does not exist.")
    verify_private_destination(manifest, "Manifest")
    verify_private_destination(destination, "Output folder")
    if destination.exists():
        raise ValueError("Output folder already exists. Choose a new folder for each pilot run.")
    cases = load_cases(manifest)
    version = execute(binary, ["--version"], timeout=30)
    version_text = version["stdout"].strip()
    if version["exit"] != 0 or not (version_text == EXPECTED_PREFIX or
                                  version_text.startswith(EXPECTED_PREFIX + "+")):
        raise ValueError(f"Expected published {EXPECTED_PREFIX} executable (not a development build).")
    destination.mkdir(parents=True, exist_ok=False)
    raw_folder = destination / "private-raw"
    raw_folder.mkdir()
    rows = []
    review = []
    for case in cases:
        case_id, project = case["case_id"], case["path"]
        print(f"Auditing {case_id} ({case['kind']})...", flush=True)
        scan_exit = inspect_exit = None
        if case["kind"] == "pbip":
            scan = execute(binary, ["scan", project, "--no-color"], args.timeout)
            inspect = execute(binary, ["inspect", project, "--measures", "--no-color"], args.timeout)
            scan_exit, inspect_exit = scan["exit"], inspect["exit"]
            (raw_folder / f"{case_id}-scan.txt").write_text(scan["stdout"] + "\nSTDERR:\n" + scan["stderr"], encoding="utf-8")
            (raw_folder / f"{case_id}-inspect.txt").write_text(inspect["stdout"] + "\nSTDERR:\n" + inspect["stderr"], encoding="utf-8")
        audit = execute(binary, ["audit", project, "--format", "json"], args.timeout)
        (raw_folder / f"{case_id}-audit.json").write_text(audit["stdout"], encoding="utf-8")
        (raw_folder / f"{case_id}-stderr.txt").write_text(audit["stderr"], encoding="utf-8")
        try:
            document = json.loads(audit["stdout"])
            if not isinstance(document, dict) or document.get("schemaVersion") != "1.0.0":
                document = None
        except (ValueError, TypeError):
            document = None
        consistent = None
        if args.repeat > 1:
            check = execute(binary, ["audit", project, "--format", "json"], args.timeout)
            try:
                again = json.loads(check["stdout"])
            except (ValueError, TypeError):
                again = None
            consistent = bool(document and isinstance(again, dict)
                              and check["exit"] == audit["exit"]
                              and signature(document) == signature(again))
        rows.append(summarize_case(case, document, audit["exit"], scan_exit, inspect_exit,
                                   consistent, audit.get("elapsed_seconds")))
        if document:
            for item in document.get("diagnostics", []):
                review.append({
                    "case_id": case_id, "finding_id": finding_id(case_id, item),
                    "rule_id": item.get("ruleId", ""),
                    "severity": item.get("severity", ""),
                    "object_path": safe_spreadsheet(item.get("location", {}).get("objectPath", "")),
                    "file_path": safe_spreadsheet(item.get("location", {}).get("filePath", "") or ""),
                    "line": item.get("location", {}).get("line", ""),
                    "message": safe_spreadsheet(item.get("message", "")),
                    "classification": "", "notes": "",
                })
    with (destination / "private-review.csv").open("w", newline="", encoding="utf-8-sig") as stream:
        writer = csv.DictWriter(stream, fieldnames=CSV_FIELDS)
        writer.writeheader()
        writer.writerows(review)
    report = {
        "schema": RUN_SCHEMA,
        "tool_version": version["stdout"].strip().splitlines()[0],
        "generated_at_utc": dt.datetime.now(dt.timezone.utc).isoformat(timespec="seconds"),
        "project_count": len(cases),
        "projects": rows,
        "reviewed_findings": 0,
        "note": "No original paths, object names or finding messages are included. Review before sharing.",
    }
    (destination / "sanitized-summary.json").write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"Pilot finished: {len(cases)} cases; "
          f"{sum(sum(r['findings_by_rule'].values()) for r in rows)} findings.")
    print("PRIVATE files: private-raw/, private-review.csv; never upload or commit.")
    print("Only sanitized-summary.json is intended for sharing after manual review.")
    print("Next: classify all rows in private-review.csv, then run 'summarize'.")
    return 0


def pilot_summarize(args: argparse.Namespace) -> int:
    root = pathlib.Path(args.out).expanduser().resolve()
    verify_private_destination(root, "Pilot output folder")
    report = json.loads((root / "sanitized-summary.json").read_text(encoding="utf-8"))
    if report.get("schema") != RUN_SCHEMA:
        raise ValueError("Unsupported pilot report schema.")
    with (root / "private-review.csv").open("r", newline="", encoding="utf-8-sig") as stream:
        rows = list(csv.DictReader(stream))
    classifications = Counter()
    rule_classes: dict[str, Counter] = {}
    for item in rows:
        label = item["classification"].strip().lower()
        if label and label not in CLASSES:
            raise ValueError(f"Unknown review classification for {item['case_id']}.")
        if not label:
            label = "unreviewed"
        classifications[label] += 1
        rule = item["rule_id"]
        rule_classes.setdefault(rule, Counter())[label] += 1
    total = len(rows)
    reviewed = total - classifications["unreviewed"]
    outcome = {
        "schema": "pbiq-pilot-review-v1", "project_count": report["project_count"],
        "total_findings": total, "reviewed_findings": reviewed,
        "classifications": dict(sorted(classifications.items())),
        "by_rule": {k: dict(sorted(v.items())) for k, v in sorted(rule_classes.items())},
        "all_evaluated": all(x["evaluated"] for x in report["projects"]),
        "all_repeat_consistent": all(x["repeat_consistent"] is True for x in report["projects"]),
        "project_statuses": [{"case_id": x["case_id"], "evaluated": x["evaluated"],
                              "audit_status": x["audit_status"]} for x in report["projects"]],
    }
    (root / "sanitized-review.json").write_text(json.dumps(outcome, indent=2) + "\n", encoding="utf-8")
    print(f"Reviewed: {reviewed}/{total}; false positives: {classifications['false_positive']}; "
          f"intentional: {classifications['intentional']}; uncertain: {classifications['uncertain']}")
    print("Generated sanitized-review.json. Review this file before sharing.")
    return 0


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Privacy-conscious PBIQ real-model pilot. No uploads.")
    sub = parser.add_subparsers(dest="command", required=True)
    run = sub.add_parser("run", help="Audit 3 to 5 projects and create private review sheet.")
    run.add_argument("--manifest", required=True)
    run.add_argument("--pbiq", required=True, help="Path to installed published PBIQ executable.")
    run.add_argument("--out", required=True, help="New folder outside every Git repository.")
    run.add_argument("--repeat", type=int, choices=(1, 2), default=2)
    run.add_argument("--timeout", type=int, default=180)
    summary = sub.add_parser("summarize", help="Summarize manually classified findings.")
    summary.add_argument("--out", required=True)
    return parser


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    try:
        if args.command == "run":
            if args.timeout < 10 or args.timeout > 3600:
                raise ValueError("Timeout must be 10..3600 seconds.")
            return pilot_run(args)
        return pilot_summarize(args)
    except (ValueError, OSError, KeyError, json.JSONDecodeError) as error:
        # Avoid printing local paths and raw project errors in the terminal.
        print(f"PILOT ERROR: {str(error).split(' (')[0]}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
