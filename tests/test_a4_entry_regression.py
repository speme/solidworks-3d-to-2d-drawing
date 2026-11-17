"""Regression for the user's A4 plate failure, opt in with --native."""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import unittest
ROOT=Path(__file__).resolve().parents[1]
NATIVE='--native' in sys.argv
if NATIVE: sys.argv.remove('--native')


@unittest.skipUnless(NATIVE,'requires local failure report and SolidWorks')
class A4EntryRegression(unittest.TestCase):
    def test_actual_a4_plate_has_outputs_and_no_text_collisions(self):
        report=Path(os.environ.get("AUTODRAWING_A4_FAILURE_REPORT", ""))
        if not report.is_file(): self.skipTest('local failure report unavailable')
        failed=json.loads(report.read_text(encoding='utf-8-sig'))['Jobs'][0]
        model=Path(failed['Model']); original=hashlib.sha256(model.read_bytes()).hexdigest()
        options=dict(failed['Options']); options['AutoReviewPdf']=True
        root=ROOT/'artifacts/v1.3.0/a4-retest'; root.mkdir(parents=True,exist_ok=True)
        request={'SchemaVersion':'1.0','RequestId':'A4-112-3-1-07-final','Models':[{'Path':str(model)}],'Options':options}
        path=root/'request-final.json'; path.write_text(json.dumps(request,ensure_ascii=False),encoding='utf-8')
        proc=subprocess.run([str(ROOT/'src/AutoDrawing/bin/Release/net48/AutoDrawing.Cli.exe'),'generate','--request',str(path)],capture_output=True,encoding='utf-8',timeout=240)
        (root/'result-final.json').write_text(proc.stdout,encoding='utf-8'); (root/'progress-final.log').write_text(proc.stderr,encoding='utf-8')
        self.assertEqual(proc.returncode,0,proc.stdout+proc.stderr)
        job=json.loads(proc.stdout)['Jobs'][0]
        self.assertEqual(job['GeneratorVersion'],'1.0.0')
        self.assertIn('AutoDrawing.dll',job['GeneratorAssemblyPath'])
        self.assertEqual(hashlib.sha256(model.read_bytes()).hexdigest(),original)
        self.assertEqual(job['SheetSizeMm'],[297,210])
        self.assertEqual(job['SheetInitialization']['EmbeddedSegmentsReopened'],job['SheetInitialization']['EmbeddedSegmentsBefore'])
        self.assertEqual(job['SheetInitialization']['Mode'],'preserve_embedded')
        self.assertEqual(sorted(round(c['DistanceMm'],4) for c in job['ChamferCallouts']),[3,3,10,10])
        self.assertTrue(all(c['Reopened'] and c['TextStyle']==4 and c['Text'] in ('C3','C10') and len(c['Entities'])==2 for c in job['ChamferCallouts']))
        self.assertEqual(len(job['RemovedChamferDimensions']),10)
        self.assertEqual(len(job['Dimensions']),12)
        self.assertEqual(sum(d['Type']==10 for d in job['Dimensions']),4)
        self.assertEqual(sorted(round(d['ValueSI']*1000,4) for d in job['Dimensions'] if d['Type']!=10),[14,15,15,24,55,55,70,160])
        for key in ('Drawing','Pdf','Dxf'): self.assertTrue(Path(job[key]).is_file())
        audit=json.loads(Path(job['PdfReview']).read_text(encoding='utf-8'))
        self.assertTrue(audit['all_dimensions_matched'])
        self.assertTrue(audit['all_chamfer_callouts_matched'])
        self.assertFalse(any(page['collisions'] for page in audit['pages']))
        import ezdxf
        drawing=ezdxf.readfile(job['Dxf']); self.assertFalse(drawing.audit().errors)
        # SW exports native chamfer dimensions as leader/text entities in DXF.
        self.assertEqual(len(drawing.modelspace().query('DIMENSION')),sum(d['Type']!=10 for d in job['Dimensions']))
        texts=[''.join(e.plain_text() if e.dxftype()=='MTEXT' else e.dxf.text for e in block if e.dxftype() in ('MTEXT','TEXT')) for block in drawing.blocks if block.name.startswith('SW_CHAMFER_DIMENSION_')]
        self.assertEqual(texts.count('C3'),2); self.assertEqual(texts.count('C10'),2)


if __name__=='__main__': unittest.main()
