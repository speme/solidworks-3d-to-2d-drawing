"""Validate actual native output; --native regenerates VH400 and the R3 fixture."""
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import unittest
import re

ROOT = Path(__file__).resolve().parents[1]
CHECK_RESULT = '--check-result' in sys.argv
if CHECK_RESULT:
    sys.argv.remove('--check-result')
NATIVE = '--native' in sys.argv or CHECK_RESULT
if '--native' in sys.argv:
    sys.argv.remove('--native')


@unittest.skipUnless(NATIVE, 'requires SolidWorks and local VH400 model')
class DimensionReductionTests(unittest.TestCase):
    def test_reduction_preserves_geometry_and_radius_quantity(self):
        root = ROOT / 'artifacts/v1.3.3'
        if CHECK_RESULT:
            self.check_jobs(json.loads((root / 'result.json').read_text(encoding='utf-8-sig'))['Jobs'],
                            json.loads((root / 'source-hashes.json').read_text(encoding='utf-8-sig')))
            return
        request_path = root / 'request.json'
        request = json.loads(request_path.read_text(encoding='utf-8-sig'))
        hashes = {m['Path']: hashlib.sha256(Path(m['Path']).read_bytes()).hexdigest()
                  for m in request['Models']}
        proc = subprocess.run([str(ROOT / 'src/AutoDrawing/bin/Release/net48/AutoDrawing.Cli.exe'),
                               'generate', '--request', str(request_path)],
                              capture_output=True, encoding='utf-8', timeout=480)
        (root / 'test-result.json').write_text(proc.stdout, encoding='utf-8')
        (root / 'test-progress.log').write_text(proc.stderr, encoding='utf-8')
        self.assertEqual(proc.returncode, 0, proc.stdout + proc.stderr)
        self.check_jobs(json.loads(proc.stdout)['Jobs'], hashes)

    def check_jobs(self, jobs, hashes):
        for j in jobs:
            self.assertIsNone(j['Error'])
            self.assertEqual(hashlib.sha256(Path(j['Model']).read_bytes()).hexdigest(), hashes[j['Model']])
            for field in ('Drawing', 'Pdf', 'Dxf'):
                self.assertTrue(Path(j[field]).is_file())
            ids = {d['View'] + '|' + d['FullName'].split('@')[0] for d in j['Dimensions']}
            for removal in j['RemovedRedundantDimensions']:
                self.assertTrue(removal['RetainedDimensions'])
                for keeper in removal['RetainedDimensions']:
                    self.assertIn(keeper.split('@')[0], ids)
                self.assertNotEqual(removal['Start'], removal['End'])
            if 'SMC' in j['Model']:
                self.assertLess(len(j['Dimensions']), 59)
                self.assertGreaterEqual(len(j['RemovedRedundantDimensions']), 8)
                self.assertTrue(any(r['Reason'] == 'closed_chain' for r in j['RemovedRedundantDimensions']))
                self.assertEqual(len(j['RadiusCallouts']), 1)
                r = j['RadiusCallouts'][0]
                self.assertEqual(r['Quantity'], 4)
                self.assertEqual(len(set(r['MemberGeometries'])), 4)
                self.assertTrue(r['Reopened'])
                self.assertIn('4X', r['Text'])
                self.assertAlmostEqual(r['RadiusMm'], 6.5, 5)
                import fitz
                with fitz.open(j['Pdf']) as pdf:
                    text = re.sub(r'\s+', '', ''.join(page.get_text() for page in pdf)).upper()
                self.assertIn('4XR6.50', text)
                import ezdxf
                dxf = ezdxf.readfile(j['Dxf'])
                self.assertFalse(dxf.audit().errors)
                texts = ''.join(e.plain_text() if e.dxftype() == 'MTEXT' else e.dxf.text
                                for block in dxf.blocks for e in block if e.dxftype() in ('TEXT', 'MTEXT'))
                self.assertIn('4XR6.50', re.sub(r'\s+', '', texts).upper())
                # Equal 12.5 sizes on X and Z measure different recesses and must both survive.
                self.assertEqual(sum(abs(d['ValueSI'] - .0125) < 1e-8 for d in j['Dimensions']), 2)
            else:
                self.assertEqual(len(j['RadiusCallouts']), 1)
                self.assertEqual(j['RadiusCallouts'][0]['Quantity'], 1)
                self.assertEqual(len(j['Dimensions']), 4)


if __name__ == '__main__':
    unittest.main()
