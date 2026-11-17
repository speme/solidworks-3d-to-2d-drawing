import json
from pathlib import Path
import unittest

ROOT = Path(__file__).resolve().parents[1]


class GearInferenceNativeTests(unittest.TestCase):
    def test_missing_properties_filled_by_original_observations(self):
        path = ROOT / 'artifacts/v1.3.6/gear-result.json'
        if not path.exists() or not path.stat().st_size:
            self.skipTest('native gear formula run not completed')
        j = json.loads(path.read_text(encoding='utf-8-sig'))['Jobs'][0]
        self.assertIsNone(j['Error'])
        self.assertEqual(j['Recognition']['PartClass'], 'gear')
        self.assertIsNone(j['Rack'])
        gear = j['Gear']
        self.assertEqual(gear['Parameters']['Module'], '2')
        self.assertEqual(gear['Parameters']['Teeth'], '24')
        self.assertEqual(gear['Parameters']['PressureAngle'], '20')
        self.assertEqual(gear['Sources']['Module'], 'formula:ModuleMm')
        self.assertEqual(gear['Sources']['Teeth'], 'formula:Teeth')
        self.assertEqual(gear['Sources']['PressureAngle'], 'formula:PressureAngleDeg')
        self.assertTrue(gear['Reopened'])
        self.assertGreater(len(gear['Calculation']['Derivations']), 2)
        self.assertEqual(j['AutoDimensions'], [])
        self.assertLessEqual(len(j['Dimensions']), 4)
        self.assertNotIn('StartAngleDeg', gear['Calculation']['Values'])
        for key in ('Drawing', 'Pdf', 'Dxf'):
            self.assertGreater(Path(j[key]).stat().st_size, 0)


if __name__ == '__main__':
    unittest.main()
