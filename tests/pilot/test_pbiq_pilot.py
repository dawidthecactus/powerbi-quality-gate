"""Pure stdlib unit tests; no company models, Power BI, or .NET required."""
import argparse
import csv
import importlib.util
import json
import pathlib
import tempfile
import unittest
from unittest.mock import patch

SCRIPT = pathlib.Path(__file__).resolve().parents[2] / "scripts" / "pbiq-pilot.py"
spec = importlib.util.spec_from_file_location("pbiq_pilot", SCRIPT)
pilot = importlib.util.module_from_spec(spec)
spec.loader.exec_module(pilot)


def fake_document() -> dict:
    return {
        "schemaVersion": "1.0.0",
        "project": {"name": "Highly Secret.pbip", "modelName": "Corporate model"},
        "result": {"status": "pass_with_advisories", "exitCode": 0},
        "summary": {"rulesEvaluated": 4, "errors": 0, "warnings": 1,
                    "information": 0, "suppressed": 0},
        "coverage": {"measures": {"visible": 2, "checkedNumeric": 0,
                                  "unknownType": 2, "hasGaps": True}},
        "rules": ["SM-001", "SM-002", "SM-003", "SM-004"],
        "diagnostics": [{"ruleId": "SM-001", "severity": "warning",
                         "message": "Customer confidential string",
                         "location": {"objectPath": "SecretTable[SecretColumn]",
                                      "filePath": "private/path.tmdl", "line": 4}}],
        "failure": None,
    }


class PilotUnitTests(unittest.TestCase):
    def test_gitrepo_detection(self):
        with tempfile.TemporaryDirectory() as home:
            root = pathlib.Path(home)
            (root / ".git").write_text("gitdir: /other/place")
            self.assertTrue(pilot.inside_git(root / "output" / "next"))
            with self.assertRaisesRegex(ValueError, "Git worktree"):
                pilot.verify_private_destination(root / "output", "Output")

    def test_manifest_must_have_three_to_five(self):
        with tempfile.TemporaryDirectory() as home:
            p = pathlib.Path(home) / "manifest.csv"
            p.write_text("case_id,kind,path\nP01,fabric,/not/here\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "3 to 5"):
                pilot.load_cases(p)

    def test_manifest_rejects_nonanonymous_identifiers(self):
        with tempfile.TemporaryDirectory() as home:
            p = pathlib.Path(home) / "manifest.csv"
            p.write_text("case_id,kind,path\nSales,fabric,/foo\nP02,pbip,/bar\nP03,pbip,/baz\n", encoding="utf-8")
            with self.assertRaisesRegex(ValueError, "Case IDs"):
                pilot.load_cases(p)

    def test_signature_stable_for_reordered_diagnostics(self):
        doc = fake_document()
        doc["diagnostics"].append({"ruleId": "SM-003", "severity": "information",
                                   "message": "x", "location": {"objectPath": "X[Y]"}})
        copy = json.loads(json.dumps(doc))
        copy["diagnostics"].reverse()
        self.assertEqual(pilot.signature(doc), pilot.signature(copy))

    def test_sanitized_summary_has_no_object_names(self):
        doc = fake_document()
        report = pilot.summarize_case({"case_id": "P01", "kind": "fabric"},
                                      doc, 0, None, None, True)
        content = json.dumps(report)
        self.assertTrue(report["evaluated"])
        self.assertEqual(1, report["findings_by_rule"]["SM-001"])
        for token in ("SecretTable", "Customer confidential", "Corporate model", "Highly Secret"):
            self.assertNotIn(token, content)

    def test_safely_classifies_structural_failure(self):
        doc = {"result": {"status": "not_evaluated", "exitCode": 1},
               "failure": {"code": "STRUCTURE_FAILED", "message": "private path"},
               "diagnostics": []}
        report = pilot.summarize_case({"case_id": "P01", "kind": "pbip"}, doc, 1, 1, 2, True)
        self.assertFalse(report["evaluated"])
        self.assertEqual("STRUCTURE_FAILED", report["failure_code"])
        self.assertNotIn("private path", json.dumps(report))

    def test_spreadsheet_formula_safe(self):
        self.assertEqual("'=HYPERLINK(1)", pilot.safe_spreadsheet("=HYPERLINK(1)"))
        self.assertEqual("SafeTable[Key]", pilot.safe_spreadsheet("SafeTable[Key]"))

    def test_end_to_end_mocked_run_and_review(self):
        with tempfile.TemporaryDirectory() as home:
            root = pathlib.Path(home)
            model = root / "model.SemanticModel"
            model.mkdir()
            binary = root / "pbiq.exe"
            binary.write_bytes(b"placeholder")
            manifest = root / "manifest.csv"
            with manifest.open("w", newline="", encoding="utf-8") as stream:
                writer = csv.writer(stream)
                writer.writerow(["case_id", "kind", "path"])
                for n in range(1, 4):
                    writer.writerow([f"P{n:02d}", "fabric", str(model)])
            out = root / "private output with spaces"
            data = json.dumps(fake_document())
            def fake_exec(_binary, args, timeout):
                if args == ["--version"]:
                    return {"exit": 0, "stdout": "0.3.0-alpha.1+abc\n", "stderr": ""}
                return {"exit": 0, "stdout": data, "stderr": ""}
            opts = argparse.Namespace(manifest=str(manifest), pbiq=str(binary),
                                      out=str(out), repeat=2, timeout=60)
            with patch.object(pilot, "execute", side_effect=fake_exec):
                self.assertEqual(0, pilot.pilot_run(opts))
            self.assertTrue((out / "sanitized-summary.json").exists())
            pub = (out / "sanitized-summary.json").read_text(encoding="utf-8")
            self.assertNotIn("Highly Secret", pub)
            self.assertNotIn("SecretTable", pub)
            self.assertIn('"project_count": 3', pub)
            with (out / "private-review.csv").open("r", encoding="utf-8-sig", newline="") as stream:
                items = list(csv.DictReader(stream))
            self.assertEqual(3, len(items))
            self.assertEqual("SecretTable[SecretColumn]", items[0]["object_path"])
            items[0]["classification"] = "false_positive"
            items[1]["classification"] = "intentional"
            items[2]["classification"] = "true_positive"
            with (out / "private-review.csv").open("w", encoding="utf-8-sig", newline="") as stream:
                writer = csv.DictWriter(stream, fieldnames=pilot.CSV_FIELDS)
                writer.writeheader()
                writer.writerows(items)
            self.assertEqual(0, pilot.pilot_summarize(argparse.Namespace(out=str(out))))
            review = json.loads((out / "sanitized-review.json").read_text())
            self.assertEqual(1, review["classifications"]["false_positive"])
            self.assertEqual(3, review["reviewed_findings"])
            self.assertTrue(review["all_repeat_consistent"])
            self.assertNotIn("SecretTable", json.dumps(review))


if __name__ == "__main__":
    unittest.main()
