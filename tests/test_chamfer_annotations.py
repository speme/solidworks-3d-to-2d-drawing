"""Native callout regression. Fixtures: python tests/create_chamfer_fixtures.py."""
import hashlib
import json
from pathlib import Path
import subprocess
import sys
import unittest
ROOT=Path(__file__).resolve().parents[1]
NATIVE='--native' in sys.argv
if NATIVE: sys.argv.remove('--native')


def dxf_chamfer_texts(path):
    import ezdxf
    d=ezdxf.readfile(path)
    assert not d.audit().errors
    return [''.join(e.plain_text() if e.dxftype()=='MTEXT' else e.dxf.text for e in b if e.dxftype() in ('MTEXT','TEXT')) for b in d.blocks if b.name.startswith('SW_CHAMFER_DIMENSION_')]


@unittest.skipUnless(NATIVE,'requires local SolidWorks and owned chamfer fixtures')
class ChamferAnnotationTests(unittest.TestCase):
    def test_profile_native_and_circular_chamfers(self):
        root=ROOT/'artifacts/v1.2.5'
        models=sorted((root/'fixtures').glob('*.SLDPRT'))
        self.assertEqual({p.stem for p in models},{'profile-30','native-45','round-45'})
        hashes={p:hashlib.sha256(p.read_bytes()).hexdigest() for p in models}
        request={'SchemaVersion':'1.0','RequestId':'chamfer-regression','Models':[{'Path':str(p)} for p in models],
                 'Options':{'Template':r'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2026\templates\gb_a3.drwdot','OutputDirectory':str(root/'regression'),'AutoReviewPdf':True}}
        path=root/'regression-request.json'; path.write_text(json.dumps(request,ensure_ascii=False),encoding='utf-8')
        proc=subprocess.run([str(ROOT/'src/AutoDrawing/bin/Release/net48/AutoDrawing.Cli.exe'),'generate','--request',str(path)],capture_output=True,encoding='utf-8',timeout=240)
        (root/'regression-result.json').write_text(proc.stdout,encoding='utf-8'); (root/'regression-progress.log').write_text(proc.stderr,encoding='utf-8')
        self.assertEqual(proc.returncode,0,proc.stdout+proc.stderr)
        jobs={Path(j['Model']).stem:j for j in json.loads(proc.stdout)['Jobs']}
        for model in models:
            self.assertEqual(hashlib.sha256(model.read_bytes()).hexdigest(),hashes[model])
            j=jobs[model.stem]; self.assertEqual(j['Status'],'review_required')
            self.assertEqual(len(j['ChamferCallouts']),1,model.stem)
            c=j['ChamferCallouts'][0]; self.assertTrue(c['Reopened']); self.assertEqual(len(c['Entities']),2)
            self.assertEqual(sum(d['Type']==10 for d in j['Dimensions']),1)
            texts=dxf_chamfer_texts(j['Dxf']); self.assertEqual(len(texts),1)
            audit=json.loads(Path(j['PdfReview']).read_text(encoding='utf-8')); self.assertTrue(audit['all_dimensions_matched'])
            self.assertTrue(audit['all_chamfer_callouts_matched'])
            self.assertFalse(any(p['collisions'] for p in audit['pages']))
            if model.stem=='profile-30':
                self.assertEqual(c['TextStyle'],2); self.assertTrue(any(abs(c['AngleDegrees']-a)<1e-5 for a in (30,60)))
                self.assertNotIn('C',texts[0]); self.assertIn('X',texts[0].upper())
                # A 3 mm hole location is unrelated to a chamfer and must survive.
                self.assertTrue(any(d['Type']!=10 and abs(d['ValueSI']-.003)<1e-7 for d in j['Dimensions']))
            else:
                expected=3 if model.stem=='native-45' else 1
                self.assertAlmostEqual(c['DistanceMm'],expected,5); self.assertEqual(c['TextStyle'],4)
                self.assertEqual(texts[0],'C'+str(expected))
                if model.stem=='round-45':
                    self.assertGreater(j['ChamferSilhouetteScans'],0)
                    self.assertEqual(j['SkippedChamfers'],[])
                    self.assertEqual(len(j['RemovedChamferDimensions']),5)
                    self.assertFalse(any(d['Type']!=10 and (abs(d['ValueSI']-.001)<1e-7 or abs(d['ValueSI']-.018)<1e-7) for d in j['Dimensions']))


if __name__=='__main__': unittest.main()
