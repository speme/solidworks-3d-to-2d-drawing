import importlib.util
from pathlib import Path
import unittest
import json
import tempfile

spec = importlib.util.spec_from_file_location("review_pdf", Path(__file__).resolve().parents[1] / "scripts/review_pdf.py")
audit = importlib.util.module_from_spec(spec)
spec.loader.exec_module(audit)


class PdfMatchingTests(unittest.TestCase):
    def test_global_assignment_does_not_steal_unique_word(self):
        # A greedy choice of word 10 for the first dimension would strand the second.
        choices = [[(1, 10, (), "25"), (3, 11, (), "25")], [(2, 10, (), "25")]]
        result = audit.assign_candidates(choices)
        self.assertEqual({i: choice[1] for i, choice in result.items()}, {0: 11, 1: 10})

    def test_word_is_not_reused(self):
        result = audit.assign_candidates([[(1, 5, (), "10")], [(2, 5, (), "10")], []])
        self.assertEqual(len(result), 1)
        self.assertEqual(result[0][1], 5)

    def test_numeric_symbols_do_not_accept_part_identifiers(self):
        self.assertEqual(audit.numeric_word("Φ6"), 6)
        self.assertEqual(audit.numeric_word("R3,5"), 3.5)
        self.assertEqual(audit.numeric_word("45°"), 45)
        self.assertEqual(audit.numeric_word("C3"), 3)
        self.assertEqual(audit.numeric_word("C10"), 10)
        self.assertEqual(audit.numeric_word("4XR6.50"), 6.5)
        self.assertEqual(audit.numeric_word("4×R6.50"), 6.5)
        self.assertIsNone(audit.numeric_word("M6"))
        self.assertIsNone(audit.numeric_word("A102"))
        self.assertIsNone(audit.numeric_word("6±0.1"))

    def test_unverified_value_cannot_claim_pdf_match(self):
        import fitz
        with tempfile.TemporaryDirectory() as folder:
            root = Path(folder)
            with fitz.open() as pdf:
                page = pdf.new_page(width=200, height=200)
                page.insert_text((20, 20), "3")
                pdf.save(root / 'drawing.pdf')
            report = root / 'drawing.report.json'
            dims = [{'FullName': name, 'View': 'Front', 'Type': 2, 'ValueSI': .003,
                     'ValueVerified': verified, 'Position': [20 * .0254 / 72, 180 * .0254 / 72]}
                    for name, verified in [('unknown', False), ('verified', True)]]
            report.write_text(json.dumps({'Drawing': str(root / 'drawing.slddrw'), 'Dimensions': dims}), encoding='utf-8')
            result = audit.review(report)
            self.assertFalse(result['dimension_links'][0]['matched'])
            self.assertIsNone(result['dimension_links'][0]['value_mm_or_deg'])
            self.assertTrue(result['dimension_links'][1]['matched'])


if __name__ == "__main__":
    unittest.main()
