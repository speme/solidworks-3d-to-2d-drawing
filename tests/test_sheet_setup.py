"""Native regression for embedded frames with stale source paths.
The source model from the reported failure is read-only; outputs receive new names.
"""
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
import unittest

ROOT=Path(__file__).resolve().parents[1]
CLI=ROOT/'src/AutoDrawing/bin/Release/net48/AutoDrawing.Cli.exe'
ROOT_OUT=ROOT/'artifacts/v1.2.2'
NATIVE='--native' in sys.argv
if NATIVE: sys.argv.remove('--native')


@unittest.skipUnless(NATIVE,'requires --native, SolidWorks and the local failure report')
class SheetSetupTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        report=Path(os.environ.get("AUTODRAWING_FAILURE_REPORT", ""))
        if not report.is_file(): raise unittest.SkipTest('缺少本地原始故障报告')
        cls.failure=json.loads(report.read_text(encoding='utf-8-sig'))['Jobs'][0]
        ROOT_OUT.mkdir(parents=True,exist_ok=True)

    def generate(self,name,options,model=None):
        model=Path(model or self.failure['Model'])
        before=hashlib.sha256(model.read_bytes()).hexdigest()
        directory=ROOT_OUT/name; directory.mkdir(parents=True,exist_ok=True)
        request={'SchemaVersion':'1.0','RequestId':name,'Models':[{'Path':str(model)}],'Options':options}
        request_path=directory/'request.json'; request_path.write_text(json.dumps(request,ensure_ascii=False),encoding='utf-8')
        result=subprocess.run([str(CLI),'generate','--request',str(request_path)],capture_output=True,encoding='utf-8',timeout=240)
        (directory/'result.json').write_text(result.stdout,encoding='utf-8'); (directory/'progress.log').write_text(result.stderr,encoding='utf-8')
        self.assertEqual(result.returncode,0,result.stdout+result.stderr)
        self.assertEqual(before,hashlib.sha256(model.read_bytes()).hexdigest(),'源模型文件被修改')
        data=json.loads(result.stdout); job=data['Jobs'][0]
        self.assertEqual(job['Status'],'review_required')
        for key in ('Drawing','Pdf','Dxf'): self.assertTrue(Path(job[key]).is_file(),key)
        return job

    def test_reported_a0_shaft_failure(self):
        options=dict(self.failure['Options']); options['AutoReviewPdf']=True
        # Same model, A0 template and user's output directory as the failed request.
        job=self.generate('reported-a0-shaft',options)
        evidence=job['SheetInitialization']
        self.assertEqual(evidence['Mode'],'preserve_embedded')
        self.assertFalse(evidence['OriginalFormatFileExists'])
        self.assertEqual(evidence['EmbeddedSegmentsBefore'],130)
        self.assertEqual(evidence['EmbeddedSegmentsAfter'],130)
        self.assertEqual(evidence['EmbeddedSegmentsReopened'],130)
        self.assertEqual(job['SheetSizeMm'],[1189,841])
        self.assertGreater(len(job['Dimensions']),0)
        import ezdxf
        dxf=ezdxf.readfile(job['Dxf']); self.assertEqual(len(dxf.audit().errors),0)
        self.assertGreater(len(dxf.modelspace().query('DIMENSION')),0)

    def test_projection_change_preserves_a3_frame(self):
        options=dict(self.failure['Options'])
        options.update(Template=r'C:\ProgramData\SolidWorks\SOLIDWORKS 2026\templates\gb_a3.drwdot',FirstAngle=False,AutoReviewPdf=True,OutputDirectory=str(ROOT_OUT/'third-angle/drawings'))
        job=self.generate('third-angle',options,ROOT/'artifacts/plate/perforated-plate.SLDPRT')
        evidence=job['SheetInitialization']
        self.assertEqual(evidence['After'][4],0)
        self.assertEqual(evidence['EmbeddedSegmentsReopened'],evidence['EmbeddedSegmentsBefore'])

    def test_size_change_preserves_embedded_frame(self):
        options=dict(self.failure['Options'])
        options.update(Template=r'C:\ProgramData\SolidWorks\SOLIDWORKS 2026\templates\gb_a3.drwdot',PaperWidthMm=450,PaperHeightMm=320,OutputDirectory=str(ROOT_OUT/'custom-size/drawings'))
        job=self.generate('custom-size',options,ROOT/'artifacts/plate/perforated-plate.SLDPRT')
        self.assertEqual(job['SheetSizeMm'],[450,320])
        evidence=job['SheetInitialization']; self.assertEqual(evidence['EmbeddedSegmentsReopened'],evidence['EmbeddedSegmentsBefore'])


if __name__=='__main__': unittest.main()
