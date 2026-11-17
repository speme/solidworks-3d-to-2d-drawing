import hashlib
import json
from pathlib import Path
import re
import unittest

ROOT = Path(__file__).resolve().parents[1]


class RackStartDatumTests(unittest.TestCase):
    def check_case(self, key, end, point, direction):
        root = ROOT / 'artifacts/v1.3.6'
        path = root / ('rack-' + key + '-result.json')
        if not path.exists() or not path.stat().st_size:
            self.skipTest('native rack datum run unavailable')
        j = json.loads(path.read_text(encoding='utf-8-sig'))['Jobs'][0]
        self.assertIsNone(j['Error'])
        rack = j['Rack']
        self.assertEqual(rack['Parameters']['Module'], '5')
        self.assertEqual(rack['Parameters']['PressureAngle'], '20')
        self.assertTrue(rack['Sources']['Module'].startswith('formula:'))
        self.assertTrue(rack['Sources']['PressureAngle'].startswith('formula:'))
        self.assertGreater(len(rack['Calculation']['Derivations']), 1)
        datum = rack['StartDatum']
        self.assertEqual(datum['End'], end)
        self.assertAlmostEqual(datum['ModelPoint'][0], point, 7)
        self.assertTrue(datum['Entity'])
        self.assertTrue(datum['Reopened'])
        self.assertIn(direction, datum['Text'])
        self.assertIn('X=0', datum['Text'])
        self.assertEqual(len(j['Dimensions']), 3)
        self.assertEqual(len(rack['Holes']), 6)
        self.assertEqual(rack['Tables'][1]['Cells'][0][1], '距起端 S')
        import fitz
        with fitz.open(j['Pdf']) as pdf:
            text = re.sub(r'\s+', '', ''.join(p.get_text() for p in pdf))
        self.assertIn('起端S(X=0)', text)
        self.assertIn(direction, text)
        import ezdxf
        dxf = ezdxf.readfile(j['Dxf'])
        texts = ''.join(e.plain_text() if e.dxftype() == 'MTEXT' else e.dxf.text for block in dxf.blocks for e in block if e.dxftype() in ('TEXT', 'MTEXT'))
        self.assertIn('起端S(X=0)', re.sub(r'\s+', '', texts))
        self.assertEqual(hashlib.sha256(Path(j['Model']).read_bytes()).hexdigest(), (root / 'source.sha256').read_text().strip())

    def test_minimum_start(self):
        self.check_case('min', 'Minimum', 0, '向右')

    def test_maximum_start(self):
        self.check_case('max', 'Maximum', .998, '向左')


if __name__ == '__main__':
    unittest.main()
