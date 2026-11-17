"""Native regressions: line layout, radius callouts, and evidence-backed gear drawings."""
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import unittest
ROOT=Path(__file__).resolve().parents[1]
NATIVE='--native' in sys.argv
if NATIVE: sys.argv.remove('--native')


@unittest.skipUnless(NATIVE,'requires local SolidWorks and create_v13_fixtures.py')
class DrawingV13Tests(unittest.TestCase):
    def test_layout_radius_and_gear_exports(self):
        root=ROOT/'artifacts/v1.3.0'
        old=json.loads((root/'native-request.json').read_text(encoding='utf-8-sig'))
        old['RequestId']='v13-final-native'; old['Options']['OutputDirectory']=str(root/'verified')
        models=[Path(m['Path']) for m in old['Models']]
        hashes={p:hashlib.sha256(p.read_bytes()).hexdigest() for p in models}
        path=root/'verified-request.json'; path.write_text(json.dumps(old,ensure_ascii=False),encoding='utf-8')
        proc=subprocess.run([str(ROOT/'src/AutoDrawing/bin/Release/net48/AutoDrawing.Cli.exe'),'generate','--request',str(path)],capture_output=True,encoding='utf-8',timeout=600)
        (root/'verified-result.json').write_text(proc.stdout,encoding='utf-8'); (root/'verified-progress.log').write_text(proc.stderr,encoding='utf-8')
        self.assertEqual(proc.returncode,0,proc.stdout+proc.stderr)
        jobs=json.loads(proc.stdout)['Jobs']
        import ezdxf,fitz
        for p,j in zip(models,jobs):
            self.assertEqual(hashlib.sha256(p.read_bytes()).hexdigest(),hashes[p])
            self.assertEqual(j['GeneratorVersion'],'1.0.0'); self.assertEqual(j['Status'],'review_required')
            for field in ('Drawing','Pdf','Dxf'): self.assertTrue(Path(j[field]).is_file())
            layout=j['AnnotationLayout']; self.assertTrue(layout['Reopened'])
            self.assertEqual(layout['GeometryOverlapsAfter'],0,p.name)
            self.assertLessEqual(layout['LineOverlapsAfter'],layout['LineOverlapsBefore'],p.name)
            audit=json.loads(Path(j['PdfReview']).read_text(encoding='utf-8')); self.assertTrue(audit['all_dimensions_matched']); self.assertTrue(audit['all_chamfer_callouts_matched'])
            self.assertFalse(any(page['collisions'] for page in audit['pages']),p.name)
            dxf=ezdxf.readfile(j['Dxf']); self.assertFalse(dxf.audit().errors)
            self.assertIn('AUTO_DIMENSIONS',dxf.layers)
            with fitz.open(j['Pdf']) as pdf:
                text=' '.join(page.get_text() for page in pdf)
                paths=[draw for page in pdf for draw in page.get_drawings() if draw.get('color')]
            self.assertTrue(any(max(d['color'])-min(d['color'])>.2 for d in paths),'dimension colour must survive PDF export')
            if p.stem=='112-3-1-07':
                self.assertEqual(len(j['ChamferCallouts']),4); self.assertEqual(len(j['RemovedChamferDimensions']),10)
                self.assertEqual(layout['LineOverlapsAfter'],0)
                self.assertGreater(layout['SharedWitnessPairsAfter'],0)
                self.assertLess(layout['CrossingsAfter'],layout['CrossingsBefore'])
            elif p.stem=='radius-3':
                self.assertEqual(len(j['RadiusCallouts']),1)
                radius=j['RadiusCallouts'][0]; self.assertAlmostEqual(radius['RadiusMm'],3); self.assertTrue(radius['Reopened'])
                self.assertEqual(len(radius['ReplacedAuxiliaryDimensions']),3)
                self.assertEqual(sorted(round(d['ValueSI']*1000,4) for d in j['Dimensions']),[3,10,60,100])
                self.assertIn('R3',text.replace(' ',''))
            elif p.stem=='spur-gear-m2-z24':
                gear=j['Gear']; self.assertTrue(gear['Reopened']); self.assertEqual(gear['Parameters']['Module'],'2'); self.assertEqual(gear['Parameters']['Teeth'],'24')
                self.assertEqual(gear['Sources']['Module'],'model_property::Module'); self.assertNotIn('Accuracy',gear['Parameters'])
                self.assertEqual(j['Recognition']['PartClass'],'gear'); self.assertLessEqual(len(j['Dimensions']),4)
                self.assertEqual(sorted(round(d['ValueSI']*1000,4) for d in j['Dimensions']),[10,10,52])
                self.assertIn('齿轮参数',text); self.assertIn('未提供',text)
                self.assertIn('20',text); self.assertIn('24',text)


if __name__=='__main__': unittest.main()
