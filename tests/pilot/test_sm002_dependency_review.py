"""Synthetic-only regression tests for private SM-002.5c review tooling."""

import csv
import importlib.util
import json
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "sm002-dependency-review.py"
module_spec = importlib.util.spec_from_file_location("dependency_review", SCRIPT)
review = importlib.util.module_from_spec(module_spec)
module_spec.loader.exec_module(review)


def row(case, measure, reference, category="unresolved_unqualified", status="", notes=""):
    return {
        "case_id": case, "measure_table": "PrivateTable", "measure_name": measure,
        "reference_table": "", "reference_name": reference,
        "candidate_kind": category, "review_status": status, "notes": notes,
    }


class DependencyReviewTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="pbiq-dependency-review-")
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)

    def csv(self, name, rows, fields=None):
        file = self.root / name
        with file.open("w", newline="", encoding="utf-8") as stream:
            writer = csv.DictWriter(stream, fieldnames=fields or list(review.FIELDS))
            writer.writeheader()
            writer.writerows(rows)
        return str(file)

    def test_summary_counts_review_statuses_and_categories(self):
        rows = [row("P01", "M1", "R1", status="confirmed_missing"),
                row("P01", "M2", "R2", status="confirmed_valid"),
                row("P01", "M3", "R3", category="unknown_qualified_member", status="uncertain"),
                row("P01", "M4", "R4")]
        case, data = review.load_review(self.csv("private.csv", rows))
        result = review.summarize(case, data)
        self.assertEqual((4, 3, 2, 1), (result["candidates"], result["reviewed"],
                                      result["conclusive"], result["unreviewed"]))
        self.assertEqual(1, result["by_candidate_kind"]["unknown_qualified_member"]["uncertain"])
        self.assertEqual(3, result["by_candidate_kind"]["unresolved_unqualified"]["total"])

    def test_comparison_is_case_insensitive_and_counts_changes(self):
        old = self.csv("before.csv", [row("P01", "SomeMeasure", "Alias"),
                                      row("P01", "Missing", "Other")])
        new = self.csv("after.csv", [row("P01", "somemeasure", "alias"),
                                     row("P01", "NewMeasure", "Another")])
        case1, rows1 = review.load_review(old)
        case2, rows2 = review.load_review(new)
        result = review.compare(case1, rows1, case2, rows2)
        self.assertEqual((2, 2, 1, 1, 1),
                         tuple(result[key] for key in (
                             "baseline_candidates", "current_candidates", "unchanged_candidates",
                             "no_longer_flagged", "newly_flagged")))

    def test_output_contains_no_private_identifiers_or_notes(self):
        private = row("P01", "SecretMeasure", "SecretColumn", notes="SecretConnectionKey")
        case, rows = review.load_review(self.csv("private.csv", [private]))
        result = review.summarize(case, rows)
        target = self.root / "aggregate.json"
        review.write_report(str(target), result)
        content = target.read_text(encoding="utf-8")
        for secret in ("SecretMeasure", "SecretColumn", "SecretConnectionKey", str(self.root)):
            self.assertNotIn(secret, content)
        self.assertEqual("P01", json.loads(content)["case_id"])

    def test_duplicate_candidates_rejected(self):
        file = self.csv("private.csv", [row("P01", "M", "R"), row("P01", "m", "r")])
        with self.assertRaises(review.ReviewError):
            review.load_review(file)

    def test_empty_review_with_explicit_case_yields_zero_summary(self):
        filename = self.csv("empty.csv", [])
        case, data = review.load_review(filename, "P02")
        self.assertEqual("P02", case)
        self.assertEqual(0, review.summarize(case, data)["candidates"])

    def test_compare_before_nonempty_and_after_empty(self):
        before = self.csv("before.csv", [row("P02", "M", "R")])
        after = self.csv("after.csv", [])
        target = str(self.root / "compare.json")
        self.assertEqual(0, review.main(["compare", "--before", before, "--after", after,
                                         "--case-id", "P02", "--out", target]))
        report = json.loads(Path(target).read_text(encoding="utf-8"))
        self.assertEqual(1, report["no_longer_flagged"])
        self.assertEqual(0, report["current_candidates"])

    def test_unapproved_case_rejected(self):
        file = self.csv("private.csv", [row("PRIVATE", "M", "R")])
        with self.assertRaises(review.ReviewError):
            review.load_review(file)

    def test_mixed_cases_rejected(self):
        file = self.csv("private.csv", [row("P01", "M", "R"), row("P02", "N", "R")])
        with self.assertRaises(review.ReviewError):
            review.load_review(file)

    def test_comparison_different_cases_rejected(self):
        a = review.load_review(self.csv("a.csv", [row("P01", "M", "R")]))
        b = review.load_review(self.csv("b.csv", [row("P02", "M", "R")]))
        with self.assertRaises(review.ReviewError):
            review.compare(a[0], a[1], b[0], b[1])

    def test_invalid_kind_and_status_rejected(self):
        files = [self.csv("badkind.csv", [row("P01", "M", "R", category="not_a_category")]),
                 self.csv("badstatus.csv", [row("P01", "M", "R", status="correct")])]
        for file in files:
            with self.subTest(file=file), self.assertRaises(review.ReviewError):
                review.load_review(file)

    def test_unexpected_columns_rejected(self):
        fields = list(review.FIELDS) + ["secret_extra"]
        data = row("P01", "M", "R")
        data["secret_extra"] = "secret"
        file = self.csv("private.csv", [data], fields)
        with self.assertRaises(review.ReviewError):
            review.load_review(file)

    def test_empty_review_rejected(self):
        file = self.csv("empty.csv", [])
        with self.assertRaises(review.ReviewError):
            review.load_review(file)

    def test_in_git_tree_rejected_for_private_input_and_output(self):
        (self.root / ".git").mkdir()
        file = self.csv("private.csv", [row("P01", "M", "R")])
        with self.assertRaises(review.ReviewError):
            review.load_review(file)
        with self.assertRaises(review.ReviewError):
            review.write_report(str(self.root / "summary.json"), {"safe": True})

    def test_output_never_overwritten(self):
        output = self.root / "summary.json"
        output.write_text("PREVIOUS DATA", encoding="utf-8")
        with self.assertRaises(review.ReviewError):
            review.write_report(str(output), {"foo": 1})
        self.assertEqual("PREVIOUS DATA", output.read_text(encoding="utf-8"))

    def test_review_rejects_symlink_parent(self):
        child = self.root / "real"
        child.mkdir()
        link = self.root / "linked"
        try:
            link.symlink_to(child, target_is_directory=True)
        except (OSError, NotImplementedError):
            self.skipTest("Symlinks are unavailable")
        file = self.csv("real/private.csv", [row("P01", "M", "R")])
        with self.assertRaises(review.ReviewError):
            review.load_review(str(link / "private.csv"))

    def test_cli_creates_anonymous_summary(self):
        file = self.csv("private.csv", [row("P01", "M", "R", status="confirmed_missing")])
        target = str(self.root / "summary.json")
        self.assertEqual(0, review.main(["summarize", "--review", file, "--out", target]))
        report = json.loads(Path(target).read_text(encoding="utf-8"))
        self.assertEqual(1, report["confirmed_missing"])

    def test_cli_compare_reports_reduction_only(self):
        before = self.csv("before.csv", [row("P02", "M", "A"), row("P02", "N", "B")])
        after = self.csv("after.csv", [row("P02", "M", "A")])
        target = str(self.root / "compare.json")
        self.assertEqual(0, review.main(["compare", "--before", before, "--after", after, "--out", target]))
        report = json.loads(Path(target).read_text(encoding="utf-8"))
        self.assertEqual(1, report["no_longer_flagged"])
        self.assertEqual(0, report["newly_flagged"])


if __name__ == "__main__":
    unittest.main()
