import json
from pathlib import Path
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[1]
CLI = ROOT / 'src/AutoDrawing/bin/Release/net48/AutoDrawing.Cli.exe'


class GearingCliTests(unittest.TestCase):
    def run_calc(self, data):
        with tempfile.TemporaryDirectory() as folder:
            source = Path(folder) / 'measurements.json'
            source.write_text(json.dumps(data), encoding='utf-8')
            return subprocess.run([str(CLI), 'calculate-gearing', '--request', str(source)], capture_output=True, encoding='utf-8')

    def test_independent_worm_calculation(self):
        p = self.run_calc({'Family': 'worm_pair', 'StandardUnshiftedFullDepth': True,
                           'Measurements': {'AxialModuleMm': 3, 'WormStarts': 2, 'WheelTeeth': 30,
                                            'WormPitchDiameterMm': 44, 'ShaftAngleDeg': 90, 'NormalPressureAngleDeg': 20}})
        self.assertEqual(p.returncode, 0, p.stderr)
        result = json.loads(p.stdout)
        self.assertEqual(result['Values']['Ratio'], 15)
        self.assertEqual(result['Values']['CenterDistanceMm'], 67)
        self.assertGreater(len(result['Derivations']), 5)

    def test_conflicting_observations_do_not_overwrite(self):
        p = self.run_calc({'Family': 'spur_external', 'Measurements': {'ModuleMm': 2, 'Teeth': 24, 'PitchDiameterMm': 50}})
        self.assertEqual(p.returncode, 4)
        result = json.loads(p.stdout)
        self.assertEqual(result['Status'], 'conflict')
        self.assertEqual(result['Values']['PitchDiameterMm'], 50)

    def test_unknown_key_rejected(self):
        self.assertEqual(self.run_calc({'Family': 'spur_external', 'Measuremnts': {}}).returncode, 2)

    def test_no_implicit_zero_phase(self):
        p = self.run_calc({'Family': 'spur_external', 'Measurements': {'ModuleMm': 2, 'Teeth': 24, 'PressureAngleDeg': 20}})
        self.assertEqual(p.returncode, 0)
        self.assertNotIn('StartAngleDeg', json.loads(p.stdout)['Values'])


if __name__ == '__main__':
    unittest.main()
