"""Native regression for the supplier's nested M10 cosmetic thread. Requires --native."""
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


@unittest.skipUnless(NATIVE,'requires SolidWorks and the local source model')
class ThreadAnnotationTests(unittest.TestCase):
    def test_nested_supplier_thread_is_marked_and_exported(self):
        report=Path(os.environ.get("AUTODRAWING_FAILURE_REPORT", ""))
        if not report.is_file(): self.skipTest('missing original local model report')
        job=json.loads(report.read_text(encoding='utf-8-sig'))['Jobs'][0]
        model=Path(job['Model']); before=hashlib.sha256(model.read_bytes()).hexdigest()
        options=dict(job['Options']); options['AutoReviewPdf']=True
        root=ROOT/'artifacts/v1.2.3/thread-regression'; root.mkdir(parents=True,exist_ok=True)
        request={'SchemaVersion':'1.0','RequestId':'nested-thread-M10','Models':[{'Path':str(model)}],'Options':options}
        path=root/'request.json'; path.write_text(json.dumps(request,ensure_ascii=False),encoding='utf-8')
        process=subprocess.run([str(ROOT/'src/AutoDrawing/bin/Release/net48/AutoDrawing.Cli.exe'),'generate','--request',str(path)],capture_output=True,encoding='utf-8',timeout=240)
        (root/'result.json').write_text(process.stdout,encoding='utf-8'); (root/'progress.log').write_text(process.stderr,encoding='utf-8')
        self.assertEqual(process.returncode,0,process.stdout+process.stderr)
        self.assertEqual(before,hashlib.sha256(model.read_bytes()).hexdigest())
        result=json.loads(process.stdout); out=result['Jobs'][0]
        self.assertEqual(len(out['Recognition']['Threads']),1)
        source=out['Recognition']['Threads'][0]
        self.assertEqual(source['Callout'],'M10x1.5 INTERNAL; DEPTH 20 mm')
        self.assertEqual(source['DepthMm'],20)
        self.assertEqual(out['Recognition']['PrimaryView'],'Left')
        self.assertEqual(len(out['ThreadCallouts']),1)
        callout=out['ThreadCallouts'][0]
        self.assertTrue(callout['AttachedToCosmeticThread']); self.assertTrue(callout['Reopened']); self.assertFalse(callout['Dangling'])
        self.assertEqual(callout['Text'],source['Callout'])
        import fitz
        with fitz.open(out['Pdf']) as pdf:
            text=' '.join(page.get_text() for page in pdf)
        self.assertIn('M10x1.5',text); self.assertIn('DEPTH 20 mm',text)
        import ezdxf
        dxf=ezdxf.readfile(out['Dxf']); audit=dxf.audit(); self.assertEqual(len(audit.errors),0)
        texts=[]
        for block in dxf.blocks:
            for entity in block:
                if entity.dxftype()=='MTEXT': texts.append(entity.plain_text())
                if entity.dxftype()=='TEXT': texts.append(entity.dxf.text)
        self.assertIn('M10x1.5',' '.join(texts)); self.assertIn('DEPTH 20 mm',' '.join(texts))
        audit=json.loads(Path(out['PdfReview']).read_text(encoding='utf-8'))
        self.assertTrue(audit['all_thread_callouts_matched']); self.assertEqual(len(audit['thread_links']),1)
        self.assertTrue(audit['all_dimensions_matched'])


if __name__=='__main__': unittest.main()
