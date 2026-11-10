#!/usr/bin/env python3
"""Privacy-safe review and baseline comparison for SM-002.5 candidates.

This tool never emits table names, measure names, referenced identifiers, notes,
DAX snippets, or input file paths. Inputs are PRIVATE; JSON output is aggregate-only.
"""

from __future__ import annotations

import argparse
import csv
import json
import re
import sys
from collections import Counter
from pathlib import Path

FIELDS = (
    "case_id", "measure_table", "measure_name", "reference_table", "reference_name",
    "candidate_kind", "review_status", "notes",
)
KINDS = ("unknown_table", "unknown_qualified_member", "unresolved_unqualified")
STATUSES = ("confirmed_missing", "confirmed_valid", "uncertain")
CASE_PATTERN = re.compile(r"P0[1-9]\Z", re.ASCII)
MAX_BYTES = 16 * 1024 * 1024
MAX_ROWS = 10000


class ReviewError(Exception):
    """Validation errors intentionally do not include user-supplied data."""


def _outside_git(path: Path, *, output: bool) -> Path:
    if str(path).strip() == "":
        raise ReviewError("Missing private file path.")
    if path.suffix.lower() not in (".csv", ".json"):
        raise ReviewError("Unexpected private file extension.")
    original = path.absolute()
    if original.is_symlink():
        raise ReviewError("Linked private files are not accepted.")
    parent = original.parent
    if not parent.is_dir():
        raise ReviewError("Private parent directory does not exist.")
    for directory in (parent, *parent.parents):
        if directory.is_symlink():
            raise ReviewError("Linked private directories are not accepted.")
        if (directory / ".git").exists() or (directory / ".git").is_file():
            raise ReviewError("Private files must be outside Git worktrees.")
    if output:
        if original.exists():
            raise ReviewError("Output file already exists; choose a new name.")
        if path.suffix.lower() != ".json":
            raise ReviewError("Output must be a .json file.")
    else:
        if path.suffix.lower() != ".csv" or not original.is_file():
            raise ReviewError("Input must be an existing private CSV.")
        if original.stat().st_size > MAX_BYTES:
            raise ReviewError("Private CSV exceeds the safe input size limit.")
    return original


def load_review(filename: str, expected_case: str | None = None):
    if expected_case is not None and not CASE_PATTERN.fullmatch(expected_case):
        raise ReviewError("Invalid expected case ID.")
    path = _outside_git(Path(filename), output=False)
    result = {}
    case_id = None
    try:
        with path.open("r", newline="", encoding="utf-8-sig") as stream:
            reader = csv.DictReader(stream, strict=True)
            if reader.fieldnames != list(FIELDS):
                raise ReviewError("Unexpected review CSV header.")
            for rownum, row in enumerate(reader, 1):
                if rownum > MAX_ROWS:
                    raise ReviewError("Private CSV exceeds the row limit.")
                if None in row or any(value is None for value in row.values()):
                    raise ReviewError("Malformed review CSV row.")
                row_case = row["case_id"].strip()
                if not CASE_PATTERN.fullmatch(row_case):
                    raise ReviewError("Invalid anonymous case ID.")
                if expected_case is not None and row_case != expected_case:
                    raise ReviewError("Unexpected anonymous case ID.")
                if case_id is None:
                    case_id = row_case
                elif case_id != row_case:
                    raise ReviewError("Mixed anonymous case IDs.")
                if row["candidate_kind"] not in KINDS:
                    raise ReviewError("Unexpected candidate category.")
                status = row["review_status"].strip().lower()
                if status not in ("", *STATUSES):
                    raise ReviewError("Unsupported review status.")
                if not row["measure_table"] or not row["measure_name"] or not row["reference_name"]:
                    raise ReviewError("Missing required private identifier.")
                key = tuple(row[field].casefold() for field in (
                    "measure_table", "measure_name", "reference_table", "reference_name",
                    "candidate_kind",
                ))
                if key in result:
                    raise ReviewError("Duplicate candidate in private review.")
                result[key] = (row["candidate_kind"], status)
    except (UnicodeError, csv.Error, OSError, ValueError) as exc:
        raise ReviewError("Unable to read the private review CSV safely.") from exc
    if case_id is None:
        if expected_case is None:
            raise ReviewError("Empty CSV requires an explicit anonymous case ID.")
        case_id = expected_case
    return case_id, result


def summarize(case_id, rows):
    counts = Counter(status or "unreviewed" for _, status in rows.values())
    reviewed = sum(counts[s] for s in STATUSES)
    conclusive = counts["confirmed_missing"] + counts["confirmed_valid"]
    by_kind = {}
    for kind in KINDS:
        items = [status for category, status in rows.values() if category == kind]
        kind_counts = Counter(status or "unreviewed" for status in items)
        by_kind[kind] = {"total": len(items), **{
            state: kind_counts[state] for state in ("unreviewed", *STATUSES)
        }}
    return {
        "schema_version": "sm002.5c.review.v1", "case_id": case_id,
        "candidates": len(rows), "reviewed": reviewed,
        "conclusive": conclusive, "unreviewed": counts["unreviewed"],
        "confirmed_missing": counts["confirmed_missing"],
        "confirmed_valid": counts["confirmed_valid"],
        "uncertain": counts["uncertain"],
        "by_candidate_kind": by_kind,
        "note": "Manual review outcomes, not certified DAX validity or model-wide accuracy.",
    }


def compare(before_case, before, after_case, after):
    if before_case != after_case:
        raise ReviewError("Comparison case IDs differ.")
    before_keys = set(before)
    after_keys = set(after)
    return {
        "schema_version": "sm002.5c.compare.v1", "case_id": before_case,
        "baseline_candidates": len(before_keys),
        "current_candidates": len(after_keys),
        "unchanged_candidates": len(before_keys & after_keys),
        "no_longer_flagged": len(before_keys - after_keys),
        "newly_flagged": len(after_keys - before_keys),
        "note": "Diff by local object identifiers; compare only unchanged model snapshots."
    }


def write_report(filename: str, report: dict) -> None:
    target = _outside_git(Path(filename), output=True)
    # Opening in exclusive mode prevents clobbering prior evidence even if a
    # file appears between validation and creation.
    try:
        with target.open("x", encoding="utf-8", newline="\n") as file:
            json.dump(report, file, indent=2, ensure_ascii=True)
            file.write("\n")
    except FileExistsError as exc:
        raise ReviewError("Output file already exists; choose a new name.") from exc
    except OSError as exc:
        raise ReviewError("Unable to write the aggregate-only report.") from exc


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Local-only dependency review: aggregate counts, no object names.")
    sub = parser.add_subparsers(dest="command", required=True)
    summary = sub.add_parser("summarize", help="Aggregate manual verification statuses from PRIVATE CSV.")
    summary.add_argument("--review", required=True)
    summary.add_argument("--out", required=True)
    summary.add_argument("--case-id", help="Required if a review has zero candidates, e.g. P02.")
    diff = sub.add_parser("compare", help="Compare PRIVATE candidate CSV snapshots by identity.")
    diff.add_argument("--before", required=True)
    diff.add_argument("--after", required=True)
    diff.add_argument("--out", required=True)
    diff.add_argument("--case-id", help="Optional P0x case check; required if both inputs are empty.")
    args = parser.parse_args(argv)
    try:
        if args.command == "summarize":
            case_id, rows = load_review(args.review, args.case_id)
            report = summarize(case_id, rows)
            write_report(args.out, report)
            print(f"Case {case_id}: {report['candidates']} candidates; {report['reviewed']} reviewed; "
                  f"{report['confirmed_missing']} missing; {report['confirmed_valid']} valid; "
                  f"{report['uncertain']} uncertain; {report['unreviewed']} unreviewed.")
        else:
            before_case, before = load_review(args.before, args.case_id)
            after_case, after = load_review(args.after, before_case)
            report = compare(before_case, before, after_case, after)
            write_report(args.out, report)
            print(f"Case {before_case}: {report['baseline_candidates']} -> {report['current_candidates']}; "
                  f"{report['no_longer_flagged']} no longer flagged; {report['newly_flagged']} newly flagged.")
        print("Aggregate-only JSON created outside Git. Review it locally before sharing.")
        return 0
    except ReviewError as error:
        print(f"SM-002.5c: {error}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
