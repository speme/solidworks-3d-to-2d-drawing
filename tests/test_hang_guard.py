"""Checks the completed real-window run; no CAD operations are performed here."""
import hashlib
import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


class HangGuardTests(unittest.TestCase):
    def test_actual_vh400_ui_run_avoids_silhouette_api(self):
        root = ROOT / 'artifacts/v1.3.4'
        batches = list((root / 'ui-native').glob('batch_*.json'))
        if not batches:
            self.skipTest('real-window regression has not completed')
        batch = json.loads(max(batches, key=lambda p: p.stat().st_mtime).read_text(encoding='utf-8-sig'))
        self.assertEqual(batch['Failed'], 0)
        self.assertEqual(len(batch['Jobs']), 1)
        j = batch['Jobs'][0]
        self.assertEqual(j['GeneratorVersion'], '1.3.4')
        self.assertIsNone(j['Error'])
        self.assertEqual(j['Status'], 'review_required')
        self.assertEqual(j['ChamferSilhouetteScans'], 0)
        self.assertEqual(len(j['SkippedChamfers']), 4)
        self.assertEqual(len(j['ChamferCallouts']), 0)
        for skip in j['SkippedChamfers']:
            self.assertEqual(skip['Reason'], 'unverified_cone_silhouette')
            self.assertEqual(skip['SourceFeatureType'], 'BaseBody')
            self.assertGreater(skip['AxialSetbackMm'], 0)
            self.assertGreater(skip['RadialSetbackMm'], 0)
        for field in ('Drawing', 'Pdf', 'Dxf'):
            self.assertGreater(Path(j[field]).stat().st_size, 0)
        progress = Path(j['Drawing']).with_suffix('.progress.log').read_text(encoding='utf-8-sig')
        self.assertIn('运行版本：1.3.4', progress)
        self.assertNotIn('原生倒角轮廓组件读取', progress)
        self.assertNotIn('原生倒角轮廓扫描开始', progress)
        self.assertIn('保存重开校验', progress)
        self.assertEqual(len(j['RadiusCallouts']), 1)
        self.assertEqual(j['RadiusCallouts'][0]['Quantity'], 4)
        hashes = json.loads((root / 'source-hashes.json').read_text(encoding='utf-8-sig'))
        self.assertEqual(hashlib.sha256(Path(j['Model']).read_bytes()).hexdigest(), hashes[j['Model']])


if __name__ == '__main__':
    unittest.main()
