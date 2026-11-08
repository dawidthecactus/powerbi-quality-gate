#!/usr/bin/env python3
"""Summarize an opt-in PRIVATE PBIQ evidence review into anonymous aggregate JSON.

The input has table and measure names and MUST remain outside Git. This tool
never reads source models or sends data over a network. Summary is not an
accuracy certificate, especially when many sample rows are inconclusive.
"""
from __future__ import annotations

import argparse
import csv
import json
import pathlib
import re
import sys
from collections import Counter

FIELDS = (
    "case_id", "sample_id", "table_name", "measure_name", "evidence_source",
    "predicted_kind", "observed_kind", "notes",
)
SOURCES = {"static", "var"}
PREDICTIONS = {"numeric", "nonnumeric"}
OBSERVATIONS = {"", "numeric", "nonnumeric", "variant", "unknown"}
SCHEMA = "pbiq-sm002-evidence-validation-v1"


def ensure_outside_git(path: pathlib.Path) -> None:
    """Reject a source or output within a Git tree or through a symlinked dir."""
    for parent in (path.parent, *path.parent.parents):
        if (parent.joinpath(".git").exists()):
            raise ValueError("Review inputs and outputs must be outside Git trees.")
        if parent.is_symlink():
            raise ValueError("Linked directories are not accepted for private reviews.")


def read_review(path: pathlib.Path) -> tuple[str, list[dict[str, str]]]:
    if path.is_symlink() or not path.is_file() or path.suffix.lower() != ".csv":
        raise ValueError("Private evidence review must be an existing CSV file.")
    ensure_outside_git(path)
    with path.open("r", encoding="utf-8-sig", newline="") as stream:
        reader = csv.DictReader(stream)
        if reader.fieldnames != list(FIELDS):
            raise ValueError("Private evidence CSV has an unexpected schema.")
        rows = list(reader)
    if not rows:
        raise ValueError("Private evidence CSV contains no sample rows.")
    cases: set[str] = set()
    identifiers: set[tuple[str, str]] = set()
    for row in rows:
        if set(row) != set(FIELDS) or any(value is None for value in row.values()):
            raise ValueError("Private evidence CSV contains malformed rows.")
        case = row["case_id"]
        if not re.fullmatch(r"P0[1-9]", case):
            raise ValueError("Only anonymous case aliases P01..P09 are permitted.")
        cases.add(case)
        sample_id = row["sample_id"]
        if not re.fullmatch(r"S\d{3,5}", sample_id):
            raise ValueError("Sample identifiers have an invalid format.")
        key = (case, sample_id)
        if key in identifiers:
            raise ValueError("Duplicate anonymous sample identifier.")
        identifiers.add(key)
        if row["evidence_source"] not in SOURCES or row["predicted_kind"] not in PREDICTIONS:
            raise ValueError("Unsupported evidence source or predicted kind.")
        if row["observed_kind"].strip().lower() not in OBSERVATIONS:
            raise ValueError("Observed kind must be numeric, nonnumeric, variant or unknown.")
    if len(cases) != 1:
        raise ValueError("A private review CSV must contain only one case alias.")
    return next(iter(cases)), rows


def metrics(rows: list[dict[str, str]]) -> dict[str, int | float | None]:
    counts: Counter[str] = Counter()
    for row in rows:
        label = row["observed_kind"].strip().lower()
        if label == "":
            counts["unreviewed"] += 1
        elif label in {"variant", "unknown"}:
            counts["inconclusive"] += 1
        else:
            counts["conclusive"] += 1
            counts["matched" if label == row["predicted_kind"] else "mismatched"] += 1
    total = len(rows)
    conclusive = counts["conclusive"]
    return {
        "sampled": total,
        "reviewed": total - counts["unreviewed"],
        "unreviewed": counts["unreviewed"],
        "inconclusive": counts["inconclusive"],
        "conclusive": conclusive,
        "matched": counts["matched"],
        "mismatched": counts["mismatched"],
        "conclusive_match_rate": round(counts["matched"] / conclusive, 4)
        if conclusive else None,
    }


def summarize(case: str, rows: list[dict[str, str]]) -> dict:
    groups: dict[str, dict[str, list[dict[str, str]]]] = {}
    for source in sorted(SOURCES):
        groups[source] = {}
        for prediction in sorted(PREDICTIONS):
            subset = [row for row in rows if row["evidence_source"] == source
                      and row["predicted_kind"] == prediction]
            if subset:
                groups[source][prediction] = subset
    return {
        "schema": SCHEMA,
        "case_id": case,
        "method": "deterministic_bounded_stratified_sample_v1",
        "scope": "advisory_hints_only_not_TOM_confirmed",
        "summary": metrics(rows),
        "by_evidence_source": {
            source: {prediction: metrics(subset)
                     for prediction, subset in predictions.items()}
            for source, predictions in groups.items() if predictions
        },
        "interpretation": "Conclusive match rate is descriptive for reviewed samples only,"
                          " not model-wide precision or independently certified correctness.",
    }


def main() -> int:
    parser = argparse.ArgumentParser(description="Offline private SM-002 evidence review summary.")
    parser.add_argument("--review", required=True, help="Existing PRIVATE CSV outside Git.")
    parser.add_argument("--out", required=True, help="New aggregate-only JSON path outside Git.")
    args = parser.parse_args()
    try:
        source = pathlib.Path(args.review).expanduser().absolute()
        destination = pathlib.Path(args.out).expanduser().absolute()
        if source == destination:
            raise ValueError("Private input and summary output must be different.")
        if destination.suffix.lower() != ".json":
            raise ValueError("Summary output must use .json.")
        ensure_outside_git(destination)
        if not destination.parent.is_dir() or destination.exists():
            raise ValueError("Summary destination must be new in an existing private directory.")
        case, rows = read_review(source)
        result = summarize(case, rows)
        # Exclusive creation prevents silent overwrites of previous validation runs.
        with destination.open("x", encoding="utf-8", newline="\n") as stream:
            json.dump(result, stream, indent=2, ensure_ascii=True)
            stream.write("\n")
        totals = result["summary"]
        print(f"Case {case}: sampled {totals['sampled']}, reviewed {totals['reviewed']}, "
              f"conclusive {totals['conclusive']}, matched {totals['matched']}, "
              f"mismatched {totals['mismatched']}.")
        print("Aggregate-only JSON created outside Git. Review before sharing.")
        return 0
    except ValueError as error:
        # Validation messages above are fixed and never echo private field values.
        print(f"EVIDENCE_VALIDATION_ERROR: {error}", file=sys.stderr)
        return 2
    except (OSError, csv.Error):
        # OS errors often contain absolute filenames; suppress those details.
        print("EVIDENCE_VALIDATION_ERROR: Private file I/O or CSV parsing failed.",
              file=sys.stderr)
        return 2


if __name__ == "__main__":
    raise SystemExit(main())
