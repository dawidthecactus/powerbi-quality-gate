"""Synthetic-only tests for advisory SM-002.6a root-cause hypotheses."""
import contextlib
import csv
import importlib.util
import io
import json
import tempfile
import unittest
from pathlib import Path

SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "sm002-dependency-root-causes.py"
spec = importlib.util.spec_from_file_location("sm002_root_causes", SCRIPT)
module = importlib.util.module_from_spec(spec)
spec.loader.exec_module(module)


def item(measure, member, *, kind="unresolved_unqualified", qualifier="", status="", case="P01"):
    return {
        "case_id": case, "measure_table": "PrivateTable", "measure_name": measure,
        "reference_table": qualifier, "reference_name": member,
        "candidate_kind": kind, "review_status": status, "notes": "PrivateNote",
    }


class DependencyRootCauseTests(unittest.TestCase):
    def setUp(self):
        ctx = tempfile.TemporaryDirectory(prefix="pbiq-root-cause-")
        self.addCleanup(ctx.cleanup)
        self.root = Path(ctx.name)

    def source(self, rows, name="source.csv", *, fields=None, delimiter=","):
        path = self.root / name
        with path.open("w", newline="", encoding="utf-8-sig") as stream:
            writer = csv.DictWriter(stream, fieldnames=fields or module.COLUMNS, delimiter=delimiter)
            writer.writeheader()
            writer.writerows(rows)
        return path

    def invoke(self, rows, *, case="P01", details=True, dry=False):
        source = self.source(rows)
        public = self.root / "aggregate.json"
        private = self.root / "private.csv"
        args = ["--review", str(source), "--case-id", case, "--summary-out", str(public)]
        if details:
            args.extend(["--private-out", str(private)])
        if dry:
            args.append("--dry-run")
        with contextlib.redirect_stdout(io.StringIO()) as out:
            code = module.run(args)
        return code, out.getvalue(), public, private

    def test_same_unqualified_target_clusters_across_measures(self):
        rows = [item("M1", "Missing"), item("M2", "missing"), item("M3", "Else")]
        groups = module.cluster(rows)
        summary = module.summary("P01", rows, groups)
        self.assertEqual(2, summary["target_groups"])
        self.assertEqual(1, summary["shared_target_groups"])
        self.assertEqual(2, summary["shared_target_candidate_references"])
        self.assertEqual(2, summary["largest_group_affected_measures"])

    def test_missing_table_collapses_different_member_names(self):
        rows = [item("A", "Amount", kind="unknown_table", qualifier="Gone"),
                item("B", "Cost", kind="unknown_table", qualifier="gone")]
        self.assertEqual(1, len(module.cluster(rows)))

    def test_unknown_qualified_member_requires_same_table_and_member(self):
        rows = [item("A", "X", kind="unknown_qualified_member", qualifier="T"),
                item("B", "Y", kind="unknown_qualified_member", qualifier="T"),
                item("C", "X", kind="unknown_qualified_member", qualifier="Other")]
        self.assertEqual(3, len(module.cluster(rows)))

    def test_qualified_and_unqualified_are_not_grouped_together(self):
        rows = [item("A", "X"), item("B", "X", kind="unknown_qualified_member", qualifier="T")]
        self.assertEqual(2, len(module.cluster(rows)))

    def test_group_affected_measures_deduplicated(self):
        rows = [item("A", "Amount", kind="unknown_table", qualifier="Lost"),
                item("A", "Qty", kind="unknown_table", qualifier="Lost"),
                item("B", "Costs", kind="unknown_table", qualifier="Lost")]
        result = module.summary("P01", rows, module.cluster(rows))
        self.assertEqual((3, 2, 1, 2), (
            result["candidate_references"], result["affected_measures"],
            result["target_groups"], result["largest_group_affected_measures"]))

    def test_group_ids_stable_under_row_reordering(self):
        rows = [item("A", "a"), item("B", "b"), item("C", "b")]
        a = [(group_id, key) for group_id, key, _ in module.cluster(rows)]
        b = [(group_id, key) for group_id, key, _ in module.cluster(list(reversed(rows)))]
        self.assertEqual(a, b)

    def test_full_cli_writes_only_anonymous_console_summary(self):
        code, stdout, public, private = self.invoke([
            item("SecretMeasure", "HiddenTarget", status="confirmed_missing")])
        self.assertEqual(0, code)
        self.assertTrue(public.exists())
        self.assertTrue(private.exists())
        self.assertNotIn("SecretMeasure", stdout)
        self.assertNotIn("HiddenTarget", stdout)
        self.assertNotIn("PrivateTable", stdout)
        self.assertNotIn("PrivateNote", stdout)
        self.assertNotIn(str(self.root), stdout)
        encoded = public.read_text(encoding="utf-8")
        for name in ("SecretMeasure", "HiddenTarget", "PrivateNote", str(self.root)):
            self.assertNotIn(name, encoded)
        self.assertEqual(1, json.loads(encoded)["review_labels"]["confirmed_missing"])

    def test_private_csv_contains_group_alias_and_identifiers(self):
        code, _, _, path = self.invoke([item("M1", "X"), item("M2", "X")])
        self.assertEqual(0, code)
        with path.open("r", encoding="utf-8-sig", newline="") as stream:
            rows = list(csv.DictReader(stream))
        self.assertEqual(["G001", "G001"], [r["group_id"] for r in rows])
        self.assertEqual(["2", "2"], [r["affected_measures_in_group"] for r in rows])

    def test_zero_candidate_model_is_valid(self):
        code, _, public, private = self.invoke([], case="P02")
        self.assertEqual(0, code)
        result = json.loads(public.read_text())
        self.assertEqual((0, 0, 0), (result["candidate_references"], result["target_groups"],
                                      result["affected_measures"]))
        with private.open("r", encoding="utf-8-sig", newline="") as f:
            self.assertEqual([], list(csv.DictReader(f)))

    def test_dry_run_creates_no_files(self):
        code, stdout, public, private = self.invoke([item("A", "X")], dry=True)
        self.assertEqual(0, code)
        self.assertIn("Dry-run", stdout)
        self.assertFalse(public.exists())
        self.assertFalse(private.exists())

    def test_rejects_output_inside_git_worktree(self):
        (self.root / ".git").mkdir()
        with self.assertRaises(module.RootCauseError):
            module.checked_path(str(self.root / "output.json"), output=True, suffix=".json")

    def test_rejects_existing_output_without_overwrite(self):
        path = self.root / "aggregate.json"
        path.write_text("OLD")
        with self.assertRaises(module.RootCauseError):
            module.checked_path(str(path), output=True, suffix=".json")
        self.assertEqual("OLD", path.read_text())

    def test_rejects_duplicate_candidates_case_insensitively(self):
        path = self.source([item("A", "XX"), item("a", "xx")])
        with self.assertRaises(module.RootCauseError):
            module.load_candidates(path, "P01")

    def test_rejects_status_mismatch_and_mixed_cases(self):
        for i, rows in enumerate((
            [item("A", "X", status="engine_proven")],
            [item("A", "X", case="P01"), item("B", "Y", case="P02")],
        )):
            with self.subTest(i=i):
                path = self.source(rows, name=f"wrong-{i}.csv")
                with self.assertRaises(module.RootCauseError):
                    module.load_candidates(path, "P01")

    def test_rejects_wrong_qualifier_for_kind(self):
        path = self.source([item("A", "X", qualifier="T")])
        with self.assertRaises(module.RootCauseError):
            module.load_candidates(path, "P01")

    def test_rejects_extra_columns_and_broken_row(self):
        with self.assertRaises(module.RootCauseError):
            module.load_candidates(self.source([item("A", "X")], fields=list(module.COLUMNS)+["extra"]), "P01")
        path = self.root / "broken.csv"
        path.write_text(",".join(module.COLUMNS) + "\nP01,too,few\n", encoding="utf-8")
        with self.assertRaises(module.RootCauseError):
            module.load_candidates(path, "P01")

    def test_spreadsheet_formula_safety_for_private_identifier(self):
        code, _, _, path = self.invoke([item("=BadFormula", "@Risk")])
        self.assertEqual(0, code)
        with path.open("r", encoding="utf-8-sig", newline="") as f:
            record = next(csv.DictReader(f))
        self.assertEqual("'=BadFormula", record["measure_name"])
        self.assertEqual("'@Risk", record["reference_name"])

    def test_symlinked_private_directory_rejected_if_supported(self):
        real = self.root / "real"
        real.mkdir()
        link = self.root / "link"
        try:
            link.symlink_to(real, target_is_directory=True)
        except (OSError, NotImplementedError):
            self.skipTest("Symlinks unavailable")
        with self.assertRaises(module.RootCauseError):
            module.checked_path(str(link / "output.json"), output=True, suffix=".json")


if __name__ == "__main__":
    unittest.main()
