"""Synthetic-only coverage for Excel interoperability and privacy invariants."""

from __future__ import annotations

import contextlib
import csv
import importlib.util
import io
import tempfile
import unittest
from pathlib import Path

TOOL = Path(__file__).resolve().parents[2] / "scripts" / "sm002-review-excel.py"
spec = importlib.util.spec_from_file_location("pbiq_excel", TOOL)
excel = importlib.util.module_from_spec(spec)
spec.loader.exec_module(excel)


def item(name="Metric", reference="Alias", *, status="", notes=""):
    return ["P01", "ExampleTable", name, "", reference,
            "unresolved_unqualified", status, notes]


class ExcelReviewInteropTests(unittest.TestCase):
    def setUp(self):
        temp = tempfile.TemporaryDirectory(prefix="pbiq-excel-")
        self.addCleanup(temp.cleanup)
        self.root = Path(temp.name)

    def baseline(self, rows=None):
        path = self.root / "original.csv"
        with path.open("w", newline="", encoding="utf-8") as handle:
            writer = csv.writer(handle, quoting=csv.QUOTE_ALL)
            writer.writerow(excel.FIELDS)
            writer.writerows(rows if rows is not None else [item()])
        return str(path)

    def edited(self, rows, delimiter=";", sep=True, single_cell=False):
        path = self.root / "edited.csv"
        with path.open("w", newline="", encoding="utf-8-sig") as handle:
            if sep:
                handle.write("sep=;\r\n")
            writer = csv.writer(handle, delimiter=delimiter, quoting=csv.QUOTE_ALL)
            writer.writerow(excel.FIELDS)
            if single_cell:
                for row in rows:
                    buffer = io.StringIO()
                    nested = csv.writer(buffer, delimiter=delimiter, quoting=csv.QUOTE_ALL,
                                        lineterminator="")
                    nested.writerow(row)
                    writer.writerow([buffer.getvalue()])
            else:
                writer.writerows(rows)
        return str(path)

    def test_prepare_produces_excel_semicolon_bom_and_sep_directive(self):
        src = self.baseline([item(reference="Zażółć, ęść")])
        path = str(self.root / "export.csv")
        result = excel.prepare(src, path)
        self.assertEqual(1, result["candidates"])
        raw = Path(path).read_bytes()
        self.assertTrue(raw.startswith(b"\xef\xbb\xbfsep=;"))
        with Path(path).open(encoding="utf-8-sig", newline="") as stream:
            reader = csv.reader(stream, delimiter=";")
            self.assertEqual(["sep=", ""], next(reader))
            self.assertEqual(list(excel.FIELDS), next(reader))
            self.assertEqual("Zażółć, ęść", next(reader)[4])

    def test_roundtrip_prepare_edit_normalize(self):
        source = self.baseline([item(notes="keep")])
        prepared = str(self.root / "to-edit.csv")
        excel.prepare(source, prepared)
        # Simulate Excel saving the same dialect with a changed status.
        with Path(prepared).open(encoding="utf-8-sig", newline="") as stream:
            content = list(csv.reader(stream, delimiter=";"))
        content[2][6] = "confirmed_valid"
        self.edited(content[2:])
        target = str(self.root / "normalized.csv")
        data = excel.normalize(source, str(self.root / "edited.csv"), target)
        self.assertEqual(1, data["confirmed_valid"])
        self.assertEqual(0, data["confirmed_missing"])
        with Path(target).open(encoding="utf-8", newline="") as stream:
            reader = csv.reader(stream)
            self.assertEqual(list(excel.FIELDS), next(reader))
            self.assertEqual("confirmed_valid", next(reader)[6])

    def test_reordered_rows_restore_authoritative_order(self):
        src = self.baseline([item("M1", "R1"), item("M2", "R2")])
        edited = self.edited([item("M2", "R2", status="uncertain"),
                              item("M1", "R1", status="confirmed_missing")])
        output = str(self.root / "fixed.csv")
        excel.normalize(src, edited, output)
        with Path(output).open(encoding="utf-8", newline="") as handle:
            rows = list(csv.reader(handle))
        self.assertEqual(["M1", "M2"], [row[2] for row in rows[1:]])
        self.assertEqual(["confirmed_missing", "uncertain"], [row[6] for row in rows[1:]])

    def test_excel_whole_row_as_one_cell_recovered_safely(self):
        src = self.baseline([item(status="")])
        edited = self.edited([item(status="confirmed_missing")], delimiter=";", sep=False, single_cell=True)
        # Deliberately create canonical header and one wrapped semicolon data field.
        text = Path(edited).read_text(encoding="utf-8-sig")
        lines = text.splitlines()
        lines[0] = ",".join(excel.FIELDS)
        Path(edited).write_text("\n".join(lines) + "\n", encoding="utf-8")
        result = excel.normalize(src, edited, str(self.root / "normalized.csv"))
        self.assertEqual(1, result["single_cell_rows"])
        self.assertEqual(1, result["confirmed_missing"])

    def test_notes_with_delimiters_quotes_and_newline_preserved(self):
        src = self.baseline([item("M;quoted", "A,quoted")])
        note = '"checked"; comma, and\nnewline'
        edited = self.edited([item("M;quoted", "A,quoted", status="uncertain", notes=note)])
        target = str(self.root / "fixed.csv")
        excel.normalize(src, edited, target)
        with Path(target).open(encoding="utf-8", newline="") as handle:
            rows = list(csv.reader(handle))
        self.assertEqual(note, rows[1][7])

    def test_polish_text_and_excel_like_formula_identity_not_modified(self):
        baseline = item("'=Risk", "Żółw;ęść")
        src = self.baseline([baseline])
        excel_file = str(self.root / "excel.csv")
        excel.prepare(src, excel_file)
        target = str(self.root / "normalized.csv")
        excel.normalize(src, excel_file, target)
        with Path(target).open(encoding="utf-8", newline="") as handle:
            rows = list(csv.reader(handle))
        self.assertEqual(baseline, rows[1])

    def test_spreadsheet_modified_identifier_rejected(self):
        src = self.baseline([item("001", "R")])
        edited = self.edited([item("1", "R", status="confirmed_valid")])
        target = str(self.root / "fixed.csv")
        with self.assertRaises(excel.ExcelReviewError):
            excel.normalize(src, edited, target)
        self.assertFalse(Path(target).exists())

    def test_unexpected_extra_candidate_rejected(self):
        src = self.baseline([item("M", "R")])
        edited = self.edited([item("M", "R"), item("M2", "R2")])
        with self.assertRaises(excel.ExcelReviewError):
            excel.normalize(src, edited, str(self.root / "fix.csv"))

    def test_duplicate_candidate_rejected(self):
        src = self.baseline([item("M", "R"), item("N", "S")])
        edited = self.edited([item("M", "R"), item("M", "R")])
        with self.assertRaises(excel.ExcelReviewError):
            excel.normalize(src, edited, str(self.root / "fix.csv"))

    def test_status_must_be_approved_value(self):
        src = self.baseline()
        edited = self.edited([item(status="totally_missing")])
        with self.assertRaises(excel.ExcelReviewError):
            excel.normalize(src, edited, str(self.root / "fix.csv"))

    def test_output_never_overwritten(self):
        src = self.baseline()
        out = self.root / "export.csv"
        out.write_text("IMPORTANT EXISTING FILE", encoding="utf-8")
        with self.assertRaises(excel.ExcelReviewError):
            excel.prepare(src, str(out))
        self.assertEqual("IMPORTANT EXISTING FILE", out.read_text(encoding="utf-8"))

    def test_dry_run_never_writes(self):
        src = self.baseline()
        excel_file = str(self.root / "prepared.csv")
        self.assertEqual(1, excel.prepare(src, excel_file, dry_run=True)["candidates"])
        self.assertFalse(Path(excel_file).exists())
        edited = self.edited([item(status="confirmed_valid")])
        output = str(self.root / "normalized.csv")
        self.assertEqual(1, excel.normalize(src, edited, output, dry_run=True)["confirmed_valid"])
        self.assertFalse(Path(output).exists())

    def test_git_destination_and_source_rejected(self):
        src = self.baseline()
        repo = self.root / "git-tree"
        repo.mkdir()
        (repo / ".git").mkdir()
        with self.assertRaises(excel.ExcelReviewError):
            excel.prepare(src, str(repo / "review.csv"))
        with self.assertRaises(excel.ExcelReviewError):
            excel.prepare(str(repo / "input.csv"), str(self.root / "out.csv"))

    def test_empty_candidate_review_supported(self):
        src = self.baseline([])
        edited = self.edited([])
        target = str(self.root / "fixed.csv")
        result = excel.normalize(src, edited, target)
        self.assertEqual(0, result["candidates"])
        self.assertEqual(1, len(Path(target).read_text(encoding="utf-8").splitlines()))

    def test_single_cell_ambiguous_identity_rejected(self):
        src = self.baseline([item("M", "R")])
        edited = self.edited([item("Other", "R")], delimiter=";", sep=False, single_cell=True)
        text = Path(edited).read_text(encoding="utf-8-sig")
        lines = text.splitlines()
        lines[0] = ",".join(excel.FIELDS)
        Path(edited).write_text("\n".join(lines) + "\n", encoding="utf-8")
        with self.assertRaises(excel.ExcelReviewError):
            excel.normalize(src, edited, str(self.root / "fixed.csv"))

    def test_cli_diagnostics_do_not_contain_private_names(self):
        src = self.baseline([item("SecretMeasure", "SecretReference", status="confirmed_missing")])
        outfile = str(self.root / "excel.csv")
        printed = io.StringIO()
        with contextlib.redirect_stdout(printed):
            self.assertEqual(0, excel.main(["prepare", "--source", src, "--out", outfile]))
        for word in ("SecretMeasure", "SecretReference", str(self.root)):
            self.assertNotIn(word, printed.getvalue())

    def test_unexpected_header_rejected(self):
        src = self.baseline()
        path = self.root / "edited.csv"
        path.write_text("this_is_not_review\nfoo\n", encoding="utf-8")
        with self.assertRaises(excel.ExcelReviewError):
            excel.normalize(src, str(path), str(self.root / "fixed.csv"))


if __name__ == "__main__":
    unittest.main()
