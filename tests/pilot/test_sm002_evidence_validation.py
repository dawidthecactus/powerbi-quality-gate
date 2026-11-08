"""No real model access; only synthetic CSV rows in temporary private directories."""
from __future__ import annotations

import csv
import importlib.util
import pathlib
import sys
import tempfile
import unittest

MODULE = pathlib.Path(__file__).resolve().parents[2] / "scripts" / "sm002-evidence-validation.py"
spec = importlib.util.spec_from_file_location("pbiq_evidence_validation", MODULE)
assert spec and spec.loader
validation = importlib.util.module_from_spec(spec)
spec.loader.exec_module(validation)


def synthetic(case: str, ident: str, source: str, predicted: str,
              observed: str, private_name: str = "Sensitive_Income") -> dict:
    return dict(zip(validation.FIELDS, (
        case, ident, "Private_Entity", private_name, source, predicted, observed, "private notes",
    )))


class EvidenceValidationSummaryTests(unittest.TestCase):
    def test_reviewed_and_conclusive_metrics_with_unknown_outcomes(self) -> None:
        rows = [
            synthetic("P01", "S001", "var", "numeric", "numeric"),
            synthetic("P01", "S002", "var", "numeric", "nonnumeric"),
            synthetic("P01", "S003", "static", "nonnumeric", "unknown"),
            synthetic("P01", "S004", "static", "nonnumeric", "variant"),
            synthetic("P01", "S005", "static", "nonnumeric", ""),
        ]
        report = validation.summarize("P01", rows)
        self.assertEqual(5, report["summary"]["sampled"])
        self.assertEqual(4, report["summary"]["reviewed"])
        self.assertEqual(2, report["summary"]["inconclusive"])
        self.assertEqual(2, report["summary"]["conclusive"])
        self.assertEqual(1, report["summary"]["matched"])
        self.assertEqual(1, report["summary"]["mismatched"])
        self.assertEqual(.5, report["summary"]["conclusive_match_rate"])
        self.assertNotIn("Sensitive_Income", str(report))
        self.assertNotIn("Private_Entity", str(report))
        self.assertNotIn("private notes", str(report))

    def test_no_conclusive_review_means_no_ratio(self) -> None:
        rows = [synthetic("P02", "S001", "var", "numeric", "variant")]
        self.assertIsNone(validation.summarize("P02", rows)["summary"]["conclusive_match_rate"])

    def test_csv_roundtrip_accepts_private_names_but_not_in_summary(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            file = pathlib.Path(folder) / "private.csv"
            with file.open("w", encoding="utf-8", newline="") as stream:
                writer = csv.DictWriter(stream, fieldnames=validation.FIELDS)
                writer.writeheader()
                writer.writerow(synthetic("P01", "S001", "static", "numeric", " numeric ",
                                         "=SUM(Confidential[Value])"))
            case, rows = validation.read_review(file)
            self.assertEqual("P01", case)
            self.assertEqual(1, validation.summarize(case, rows)["summary"]["matched"])
            self.assertNotIn("Confidential", str(validation.summarize(case, rows)))

    def test_rejects_duplicate_sample_ids_and_mixed_cases(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            file = pathlib.Path(folder) / "review.csv"
            rows = [synthetic("P01", "S001", "var", "numeric", "numeric"),
                    synthetic("P01", "S001", "static", "nonnumeric", "nonnumeric")]
            self.write_review(file, rows)
            with self.assertRaisesRegex(ValueError, "Duplicate"):
                validation.read_review(file)
            rows[1]["case_id"] = "P02"
            self.write_review(file, rows)
            with self.assertRaisesRegex(ValueError, "one case"):
                validation.read_review(file)

    def test_rejects_invalid_observations_and_unexpected_columns(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            file = pathlib.Path(folder) / "review.csv"
            self.write_review(file, [synthetic("P01", "S001", "var", "numeric", "yes")])
            with self.assertRaisesRegex(ValueError, "Observed kind"):
                validation.read_review(file)
            file.write_text("foo,bar\na,b\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "schema"):
                validation.read_review(file)

    def test_rejects_git_private_review_parent(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            root = pathlib.Path(folder)
            (root / ".git").mkdir()
            with self.assertRaisesRegex(ValueError, "outside Git"):
                validation.ensure_outside_git(root / "review.csv")

    def test_rejects_empty_sample_and_unapproved_case(self) -> None:
        with tempfile.TemporaryDirectory() as folder:
            file = pathlib.Path(folder) / "review.csv"
            self.write_review(file, [])
            with self.assertRaisesRegex(ValueError, "no sample"):
                validation.read_review(file)
            self.write_review(file, [synthetic("Confidential", "S001", "var", "numeric", "numeric")])
            with self.assertRaisesRegex(ValueError, "aliases"):
                validation.read_review(file)

    @staticmethod
    def write_review(path: pathlib.Path, rows: list[dict]) -> None:
        with path.open("w", newline="", encoding="utf-8") as stream:
            writer = csv.DictWriter(stream, fieldnames=validation.FIELDS)
            writer.writeheader()
            writer.writerows(rows)


if __name__ == "__main__":
    unittest.main()
