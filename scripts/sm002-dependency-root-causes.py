#!/usr/bin/env python3
"""SM-002.6a: offline, advisory, privacy-first DAX candidate target grouping.

No DAX execution, inferred dependency graph or certified root-cause claims.
The console and summary JSON expose anonymous counts only. Optional private
CSV includes object names and MUST remain outside every Git worktree.
"""
from __future__ import annotations

import argparse
import csv
import json
import re
import sys
from collections import Counter, defaultdict
from pathlib import Path

COLUMNS = (
    "case_id", "measure_table", "measure_name", "reference_table",
    "reference_name", "candidate_kind", "review_status", "notes",
)
KINDS = ("unknown_table", "unknown_qualified_member", "unresolved_unqualified")
STATUSES = ("confirmed_missing", "confirmed_valid", "uncertain")
CASE_ID = re.compile(r"P0[1-9]\Z", re.ASCII)
MAX_FILE_BYTES = 16 * 1024 * 1024
MAX_ROWS = 10000
PRIVATE_COLUMNS = (
    "group_id", "candidate_kind", "reference_table", "reference_name",
    "candidate_count_in_group", "affected_measures_in_group", "measure_table",
    "measure_name", "review_status", "notes",
)


class RootCauseError(Exception):
    """Safe diagnostics without private file paths or object names."""


def checked_path(value: str, *, output: bool, suffix: str) -> Path:
    if not value.strip():
        raise RootCauseError("Private file path is missing.")
    path = Path(value).absolute()
    if path.suffix.casefold() != suffix:
        raise RootCauseError("Unexpected private file extension.")
    parent = path.parent
    if not parent.is_dir():
        raise RootCauseError("Private directory must already exist.")
    for node in (path, parent, *parent.parents):
        if node.is_symlink() or (hasattr(node, "is_junction") and node.is_junction()):
            raise RootCauseError("Linked private paths are not accepted.")
        if node.is_dir() and ((node / ".git").exists() or (node / ".git").is_file()):
            raise RootCauseError("Private inputs and outputs must be outside Git.")
    if output:
        if path.exists():
            raise RootCauseError("Output already exists; choose a new filename.")
    elif not path.is_file() or path.stat().st_size > MAX_FILE_BYTES:
        raise RootCauseError("Private input is missing or exceeds the size limit.")
    return path


def load_candidates(path: Path, case_id: str):
    rows = []
    identities = set()
    try:
        with path.open("r", encoding="utf-8-sig", newline="") as stream:
            reader = csv.DictReader(stream, strict=True)
            if reader.fieldnames != list(COLUMNS):
                raise RootCauseError("Unexpected private candidate CSV header.")
            for row in reader:
                if len(rows) >= MAX_ROWS:
                    raise RootCauseError("Private candidate row limit exceeded.")
                if None in row or any(v is None for v in row.values()):
                    raise RootCauseError("Malformed private CSV row.")
                if row["case_id"].strip() != case_id:
                    raise RootCauseError("Unexpected or mixed anonymous case ID.")
                kind = row["candidate_kind"]
                if kind not in KINDS:
                    raise RootCauseError("Unknown candidate kind.")
                if not all(row[x] for x in ("measure_table", "measure_name", "reference_name")):
                    raise RootCauseError("Missing required private identifier.")
                qualifier = row["reference_table"]
                if (kind == "unresolved_unqualified") != (qualifier == ""):
                    raise RootCauseError("Candidate qualifier does not match its kind.")
                status = row["review_status"].strip().lower()
                if status not in ("", *STATUSES):
                    raise RootCauseError("Invalid private review status.")
                row["review_status"] = status
                identity = tuple(row[x].casefold() for x in (
                    "measure_table", "measure_name", "reference_table", "reference_name", "candidate_kind"
                ))
                if identity in identities:
                    raise RootCauseError("Duplicate private candidate.")
                identities.add(identity)
                rows.append(row)
    except (OSError, UnicodeError, csv.Error, ValueError) as exc:
        raise RootCauseError("Could not safely read the private candidate CSV.") from exc
    return rows


def group_key(row: dict[str, str]) -> tuple[str, ...]:
    """A candidate *target hypothesis*, not a confirmed broken object.

    Unknown table is grouped by qualifier alone; other kinds require full
    reference target. Case-insensitive, consistent with TOM lookup.
    """
    kind = row["candidate_kind"]
    qualifier = row["reference_table"].casefold()
    member = row["reference_name"].casefold()
    if kind == "unknown_table":
        return kind, qualifier
    if kind == "unknown_qualified_member":
        return kind, qualifier, member
    return kind, member


def cluster(rows: list[dict[str, str]]):
    groups = defaultdict(list)
    for row in rows:
        groups[group_key(row)].append(row)
    ordered = sorted(groups.items(), key=lambda pair: (
        -len({(r["measure_table"].casefold(), r["measure_name"].casefold())
              for r in pair[1]}), -len(pair[1]), pair[0]
    ))
    return [(f"G{i:03d}", key, members) for i, (key, members) in enumerate(ordered, 1)]


def summary(case_id: str, rows: list[dict[str, str]], groups):
    affected = {(r["measure_table"].casefold(), r["measure_name"].casefold()) for r in rows}
    shared = []
    kinds = {}
    for kind in KINDS:
        kinds[kind] = {
            "candidate_references": sum(r["candidate_kind"] == kind for r in rows),
            "target_groups": sum(key[0] == kind for _, key, _ in groups),
        }
    largest_size = 0
    for _, _, members in groups:
        count = len({(r["measure_table"].casefold(), r["measure_name"].casefold())
                     for r in members})
        largest_size = max(largest_size, count)
        if count >= 2:
            shared.append((count, len(members)))
    statuses = Counter(r["review_status"] or "unreviewed" for r in rows)
    return {
        "schema_version": "sm002.6.root_cause_hypotheses.v1",
        "case_id": case_id,
        "candidate_references": len(rows),
        "affected_measures": len(affected),
        "target_groups": len(groups),
        "shared_target_groups": len(shared),
        "shared_target_candidate_references": sum(size for _, size in shared),
        "single_measure_target_groups": len(groups) - len(shared),
        "largest_group_affected_measures": largest_size,
        "by_kind": kinds,
        "review_labels": {status: statuses[status] for status in ("unreviewed", *STATUSES)},
        "note": (
            "Lexical target groups are advisory hypotheses, not proven root causes, "
            "semantic dependency edges or engine-confirmed errors. "
            "Review labels are user-entered, not independently certified."
        ),
    }


def safe_csv(value: str) -> str:
    # Spreadsheet apps can interpret cells beginning with these symbols as
    # formulas. Prefix with apostrophe rather than evaluating arbitrary input.
    if value.lstrip().startswith(("=", "+", "-", "@")):
        return "'" + value
    return value


def write_private(path: Path, groups) -> None:
    with path.open("x", encoding="utf-8-sig", newline="") as stream:
        writer = csv.DictWriter(stream, fieldnames=PRIVATE_COLUMNS, quoting=csv.QUOTE_ALL)
        writer.writeheader()
        for group_id, _, members in groups:
            count = len({(r["measure_table"].casefold(), r["measure_name"].casefold())
                         for r in members})
            for row in members:
                record = {
                    "group_id": group_id,
                    "candidate_kind": row["candidate_kind"],
                    "reference_table": row["reference_table"],
                    "reference_name": row["reference_name"],
                    "candidate_count_in_group": str(len(members)),
                    "affected_measures_in_group": str(count),
                    "measure_table": row["measure_table"],
                    "measure_name": row["measure_name"],
                    "review_status": row["review_status"],
                    "notes": row["notes"],
                }
                writer.writerow({key: safe_csv(str(value)) for key, value in record.items()})


def run(argv=None) -> int:
    parser = argparse.ArgumentParser(description="Group private DAX dependency candidates into target hypotheses.")
    parser.add_argument("--review", required=True, help="Private source CSV outside Git.")
    parser.add_argument("--case-id", required=True, help="Anonymous case P01..P09.")
    parser.add_argument("--summary-out", required=True, help="New aggregate-only JSON outside Git.")
    parser.add_argument("--private-out", help="Optional new identifying CSV outside Git.")
    parser.add_argument("--dry-run", action="store_true", help="Validate and print anonymous totals only.")
    args = parser.parse_args(argv)
    try:
        if not CASE_ID.fullmatch(args.case_id):
            raise RootCauseError("Invalid anonymous case ID.")
        source = checked_path(args.review, output=False, suffix=".csv")
        output = checked_path(args.summary_out, output=True, suffix=".json")
        private = checked_path(args.private_out, output=True, suffix=".csv") if args.private_out else None
        if len({str(p).casefold() for p in (source, output, private) if p is not None}) != (3 if private else 2):
            raise RootCauseError("Private input and outputs must have distinct paths.")
        rows = load_candidates(source, args.case_id)
        groups = cluster(rows)
        report = summary(args.case_id, rows, groups)
        if not args.dry_run:
            try:
                if private is not None:
                    write_private(private, groups)
                with output.open("x", encoding="utf-8", newline="\n") as stream:
                    json.dump(report, stream, indent=2, ensure_ascii=True)
                    stream.write("\n")
            except (OSError, UnicodeError) as exc:
                # Best-effort rollback of private CSV if aggregate write fails.
                if private is not None and private.is_file() and not output.exists():
                    try:
                        private.unlink()
                    except OSError:
                        pass
                raise RootCauseError("Unable to write new private analysis files.") from exc
        print(f"Case {args.case_id}: {report['candidate_references']} candidates; "
              f"{report['affected_measures']} affected measures; "
              f"{report['target_groups']} target groups; "
              f"{report['shared_target_groups']} shared groups; "
              f"largest affects {report['largest_group_affected_measures']} measures.")
        print("Dry-run only; no files created." if args.dry_run else
              "Aggregate-only JSON created; optional detailed CSV is PRIVATE. No object names printed.")
        return 0
    except RootCauseError as exc:
        print("PBIQ SM-002.6a: " + str(exc), file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(run())
