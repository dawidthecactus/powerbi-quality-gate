#!/usr/bin/env python3
"""Private, lossless Excel CSV workflow for PBIQ dependency reviews.

Creates an Excel-facing semicolon-separated file, then imports ONLY manually
entered review_status/notes into a fresh canonical eight-column CSV. All
identifiers are verified against the untouched source CSV before writing.
Private names/paths never appear in diagnostic output.
"""

from __future__ import annotations

import argparse
import csv
import io
import re
import sys
from collections import Counter
from pathlib import Path

FIELDS = (
    "case_id", "measure_table", "measure_name", "reference_table",
    "reference_name", "candidate_kind", "review_status", "notes",
)
KINDS = {"unknown_table", "unknown_qualified_member", "unresolved_unqualified"}
STATUSES = {"", "confirmed_missing", "confirmed_valid", "uncertain"}
MAX_BYTES = 16 * 1024 * 1024
MAX_ROWS = 10000
CASE = re.compile(r"P0[1-9]\Z", re.ASCII)


class ExcelReviewError(Exception):
    """Safe error message, never includes an identifier or a path."""


def checked_path(raw: str, *, output: bool = False) -> Path:
    if not raw or not raw.strip():
        raise ExcelReviewError("Missing private file path.")
    path = Path(raw).absolute()
    if path.suffix.lower() != ".csv":
        raise ExcelReviewError("Private review files must use .csv.")
    if not path.parent.is_dir():
        raise ExcelReviewError("Private parent directory does not exist.")
    if path.is_symlink() or (hasattr(path, "is_junction") and path.is_junction()):
        raise ExcelReviewError("Linked private files are not accepted.")
    for parent in (path.parent, *path.parent.parents):
        if parent.is_symlink() or (hasattr(parent, "is_junction") and parent.is_junction()):
            raise ExcelReviewError("Linked private directories are not accepted.")
        if (parent / ".git").exists():
            raise ExcelReviewError("Private review files must remain outside Git.")
    if output:
        if path.exists():
            raise ExcelReviewError("Output file already exists; choose another name.")
    else:
        if not path.is_file() or path.stat().st_size > MAX_BYTES:
            raise ExcelReviewError("Private input is missing or exceeds the input limit.")
    return path


def _read_rows(path: Path, delimiter: str, *, strip_sep: bool = False) -> tuple[list[str], list[list[str]]]:
    try:
        with path.open("r", encoding="utf-8-sig", newline="") as handle:
            reader = csv.reader(handle, delimiter=delimiter, strict=True)
            header = next(reader, None)
            if strip_sep and header == ["sep=", ""] and delimiter == ";":
                header = next(reader, None)
            rows = []
            for item in reader:
                if len(rows) >= MAX_ROWS:
                    raise ExcelReviewError("Private CSV exceeds row limit.")
                rows.append(item)
    except (UnicodeError, csv.Error, OSError, ValueError) as exc:
        raise ExcelReviewError("Private CSV could not be parsed safely.") from exc
    if header is None:
        raise ExcelReviewError("Private CSV is missing its header.")
    return header, rows


def _canonical_source(path: Path) -> list[list[str]]:
    header, rows = _read_rows(path, ",")
    if tuple(header) != FIELDS:
        raise ExcelReviewError("Source CSV header is not canonical.")
    found: set[tuple[str, ...]] = set()
    for row in rows:
        if len(row) != len(FIELDS):
            raise ExcelReviewError("Malformed source CSV row.")
        if not CASE.fullmatch(row[0]) or row[5] not in KINDS:
            raise ExcelReviewError("Source CSV contains an invalid case or category.")
        if not all(row[index] for index in (1, 2, 4)):
            raise ExcelReviewError("Source CSV is missing a required identifier.")
        if row[6].strip().lower() not in STATUSES:
            raise ExcelReviewError("Source CSV has an unsupported review status.")
        key = tuple(row[:6])
        if key in found:
            raise ExcelReviewError("Source CSV has duplicate references.")
        found.add(key)
    if len({r[0] for r in rows}) > 1:
        raise ExcelReviewError("Source CSV contains mixed case IDs.")
    return rows


def _parse_inner(value: str, delim: str) -> list[str] | None:
    try:
        parsed = list(csv.reader(io.StringIO(value, newline=""), delimiter=delim, strict=True))
    except csv.Error:
        return None
    return parsed[0] if len(parsed) == 1 and len(parsed[0]) == len(FIELDS) else None


def _excel_rows(path: Path, expected: dict[tuple[str, ...], list[str]]) -> tuple[dict[tuple[str, ...], tuple[str, str]], int]:
    # Header must match EXACTLY: never infer columns from arbitrary private content.
    chosen: list[list[str]] | None = None
    for delimiter in (";", ",", "\t"):
        try:
            header, rows = _read_rows(path, delimiter, strip_sep=True)
        except ExcelReviewError:
            continue
        if tuple(header) == FIELDS:
            if chosen is not None:
                raise ExcelReviewError("Ambiguous input CSV delimiter.")
            chosen = rows
    if chosen is None:
        raise ExcelReviewError("Edited Excel CSV header is not recognized.")
    if len(chosen) != len(expected):
        raise ExcelReviewError("Candidate row count changed in Excel.")

    recovered: dict[tuple[str, ...], tuple[str, str]] = {}
    single_cells = 0
    for row in chosen:
        options: list[list[str]] = []
        if len(row) == len(FIELDS):
            options.append(row)
        elif len(row) == 1:
            single_cells += 1
            for delimiter in (";", ",", "\t"):
                candidate = _parse_inner(row[0], delimiter)
                if candidate is not None:
                    options.append(candidate)
        else:
            raise ExcelReviewError("Edited CSV has a malformed field count.")
        # Exact identity is intentional: no silent case normalization or
        # replacement of identifiers changed by spreadsheet auto-formatting.
        matches = {tuple(v) for v in options if tuple(v[:6]) in expected}
        if len(matches) != 1:
            raise ExcelReviewError("An edited row is ambiguous or its identifiers changed.")
        item = list(matches.pop())
        key = tuple(item[:6])
        if key in recovered:
            raise ExcelReviewError("Edited CSV has duplicate candidate rows.")
        status = item[6].strip().lower()
        if status not in STATUSES:
            raise ExcelReviewError("Edited CSV has an unsupported review status.")
        recovered[key] = (status, item[7])
    if set(recovered) != set(expected):
        raise ExcelReviewError("Edited CSV is missing or has unexpected candidate identities.")
    return recovered, single_cells


def _write(path: Path, rows: list[list[str]], *, excel: bool) -> None:
    created = False
    try:
        # Exclusive creation is essential even if a file is created after
        # the earlier existence check. A failed write removes only OUR output.
        with path.open("x", encoding="utf-8-sig" if excel else "utf-8", newline="") as handle:
            created = True
            if excel:
                handle.write("sep=;\r\n")
            writer = csv.writer(handle, delimiter=";" if excel else ",",
                                quoting=csv.QUOTE_ALL, lineterminator="\r\n")
            writer.writerow(FIELDS)
            writer.writerows(rows)
    except FileExistsError as exc:
        raise ExcelReviewError("Output file already exists; choose another name.") from exc
    except (UnicodeError, csv.Error, OSError) as exc:
        if created:
            try:
                path.unlink()
            except OSError:
                pass
        raise ExcelReviewError("Unable to write private CSV safely.") from exc


def prepare(source: str, out: str, *, dry_run: bool = False) -> dict[str, int]:
    src = checked_path(source)
    dst = checked_path(out, output=True)
    if src == dst:
        raise ExcelReviewError("Output cannot be the source file.")
    rows = _canonical_source(src)
    if not dry_run:
        _write(dst, rows, excel=True)
    return {"candidates": len(rows)}


def normalize(source: str, edited: str, out: str, *, dry_run: bool = False) -> dict[str, int]:
    src = checked_path(source)
    edit = checked_path(edited)
    dst = checked_path(out, output=True)
    if len({src, edit, dst}) != 3:
        raise ExcelReviewError("Use three distinct private paths.")
    baseline = _canonical_source(src)
    source_map = {tuple(row[:6]): row for row in baseline}
    updated, single_cells = _excel_rows(edit, source_map)
    converted = [row[:6] + list(updated[tuple(row[:6])]) for row in baseline]
    if not dry_run:
        _write(dst, converted, excel=False)
    counts = Counter(row[6] or "unreviewed" for row in converted)
    return {"candidates": len(converted), "single_cell_rows": single_cells,
            "confirmed_missing": counts["confirmed_missing"],
            "confirmed_valid": counts["confirmed_valid"],
            "uncertain": counts["uncertain"], "unreviewed": counts["unreviewed"]}


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Excel-safe PRIVATE PBIQ review CSV bridge.")
    commands = parser.add_subparsers(dest="command", required=True)
    prep = commands.add_parser("prepare", help="Create a Polish Excel-friendly private review CSV.")
    prep.add_argument("--source", required=True)
    prep.add_argument("--out", required=True)
    prep.add_argument("--dry-run", action="store_true")
    norm = commands.add_parser("normalize", help="Verify edited CSV and restore canonical private PBIQ CSV.")
    norm.add_argument("--source", required=True)
    norm.add_argument("--edited", required=True)
    norm.add_argument("--out", required=True)
    norm.add_argument("--dry-run", action="store_true")
    args = parser.parse_args(argv)
    try:
        if args.command == "prepare":
            stats = prepare(args.source, args.out, dry_run=args.dry_run)
            print(f"Private Excel review prepared: {stats['candidates']} candidates" +
                  (" (dry-run only)." if args.dry_run else "."))
        else:
            stats = normalize(args.source, args.edited, args.out, dry_run=args.dry_run)
            print(f"Private review normalized: {stats['candidates']} candidates; "
                  f"{stats['single_cell_rows']} Excel-wrapped rows; "
                  f"missing={stats['confirmed_missing']}; valid={stats['confirmed_valid']}; "
                  f"uncertain={stats['uncertain']}; unreviewed={stats['unreviewed']}" +
                  (" (dry-run only)." if args.dry_run else "."))
        print("No private identifiers, DAX or paths printed. Review labels are user-provided, not engine validation.")
        return 0
    except ExcelReviewError as exc:
        print(f"PBIQ Excel review: {exc}", file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
