"""CLI contract tests; native generation uses only owned fixtures and templates.
Run: python tests/test_cli.py [--native]
"""
import json
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

ROOT=Path(__file__).resolve().parents[1]
CLI=ROOT/'src/AutoDrawing/bin/Release/net48/AutoDrawing.Cli.exe'
NATIVE='--native' in sys.argv
if NATIVE: sys.argv.remove('--native')


class CliTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='autodrawing-cli-')
        self.addCleanup(self.temp.cleanup)
        self.root=Path(self.temp.name)
        (self.root/'part.sldprt').write_text('non-native-validation-fixture')
        (self.root/'template.drwdot').write_text('non-native-validation-fixture')
        self.request={'SchemaVersion':'1.0','RequestId':'contract-test','Models':[{'Path':'part.sldprt'}],'Options':{'Template':'template.drwdot','OutputDirectory':'output'}}

    def call(self,request=None,*extra):
        path=self.root/'request.json'; path.write_text(json.dumps(request or self.request),encoding='utf-8')
        return subprocess.run([str(CLI),'validate','--request',str(path),*extra],cwd=ROOT,capture_output=True,encoding='utf-8',timeout=20)

    def test_validate_resolves_without_starting_solidworks(self):
        result=self.call(); self.assertEqual(result.returncode,0,result.stderr)
        data=json.loads(result.stdout); self.assertEqual(data['Status'],'validated')
        self.assertEqual(data['Resolved']['Options']['Template'],str(self.root/'template.drwdot'))

    def test_unknown_request_field_is_an_error(self):
        request=dict(self.request,Misspelled=True); result=self.call(request)
        self.assertEqual(result.returncode,2); self.assertEqual(json.loads(result.stdout)['ErrorCode'],'INVALID_REQUEST')

    def test_existing_result_is_not_overwritten(self):
        target=self.root/'result.json'; target.write_text('KEEP',encoding='utf-8')
        result=self.call(None,'--result',str(target))
        self.assertEqual(result.returncode,2); self.assertEqual(target.read_text(),'KEEP'); json.loads(result.stdout)

    def test_result_file_and_stdout_are_same_json(self):
        target=self.root/'result.json'; result=self.call(None,'--result',str(target))
        self.assertEqual(result.returncode,0); self.assertEqual(json.loads(result.stdout),json.loads(target.read_text(encoding='utf-8-sig')))

    def test_wrong_option_is_rejected(self):
        self.request['Options']['Typo']=1; result=self.call()
        self.assertEqual(result.returncode,2); self.assertIn('Typo',json.loads(result.stdout)['Error'])

    def test_version_reports_loaded_library_without_connecting_cad(self):
        proc=subprocess.run([str(CLI),'--version'],capture_output=True,encoding='utf-8',timeout=20)
        self.assertEqual(proc.returncode,0)
        data=json.loads(proc.stdout); self.assertRegex(data['Version'],r'^\d+\.\d+\.\d+$')
        self.assertTrue(Path(data['AssemblyPath']).is_file())

    @unittest.skipUnless(NATIVE,'requires SolidWorks and custom native template fixture')
    def test_native_custom_profile_and_title_property(self):
        root=ROOT/'artifacts/v1.2/native-cli'; root.mkdir(parents=True,exist_ok=True)
        profile={'SchemaVersion':'1.0','Name':'Company-A3','Options':{
            'Template':'../template/company-a3.drwdot','SheetFormat':'../template/company-a3.slddrt',
            'PaperWidthMm':420,'PaperHeightMm':297,'FixedScale':0.5,
            'LeftMarginMm':30,'RightMarginMm':20,'TopMarginMm':20,'ViewGapMm':30,
            'DrawingProperties':{'DrawingNo':'API-{ModelName}','Company':'Company-A','设计':'接口测试'},'AutoReviewPdf':True}}
        (root/'profile.json').write_text(json.dumps(profile,ensure_ascii=False),encoding='utf-8')
        request={'SchemaVersion':'1.0','RequestId':'native-company-template','TemplateProfile':'profile.json',
                 'Models':[{'Path':'../../../artifacts/plate/perforated-plate.SLDPRT'}],
                 'Options':{'OutputDirectory':'drawings','DrawingProperties':{'Company':'Company-B'}}}
        (root/'request.json').write_text(json.dumps(request),encoding='utf-8')
        result=subprocess.run([str(CLI),'generate','--request',str(root/'request.json')],cwd=self.root,capture_output=True,encoding='utf-8',timeout=180)
        (root/'stdout.json').write_text(result.stdout,encoding='utf-8'); (root/'stderr.log').write_text(result.stderr,encoding='utf-8')
        self.assertEqual(result.returncode,0,result.stdout+result.stderr)
        data=json.loads(result.stdout); self.assertEqual(data['Status'],'generated_review_required'); self.assertEqual(data['Completed'],1)
        job=data['Jobs'][0]; self.assertEqual(job['DrawingProperties']['Company'],'Company-B')
        self.assertEqual(job['SheetSizeMm'],[420,297]); self.assertTrue(job['SheetFormat'].endswith('company-a3.slddrt'))
        self.assertEqual(job['DrawingProperties']['DrawingNo'],'API-perforated-plate')
        self.assertTrue(Path(job['Dxf']).is_file()); self.assertGreater(Path(job['Dxf']).stat().st_size,0)
        self.assertEqual(Path(job['Pdf']),Path(job['Drawing']).with_suffix('.pdf'))
        import fitz
        with fitz.open(Path(job['Drawing']).with_suffix('.pdf')) as pdf:
            self.assertIn('API-perforated-plate',''.join(page.get_text() for page in pdf))
        audit=json.loads(Path(job['PdfReview']).read_text(encoding='utf-8'))
        self.assertTrue(audit['all_dimensions_matched'])

    @unittest.skipUnless(NATIVE,'requires a running SolidWorks instance')
    def test_native_batch_keeps_success_when_another_configuration_fails(self):
        root=ROOT/'artifacts/v1.2/partial-batch'; root.mkdir(parents=True,exist_ok=True)
        model=str(ROOT/'artifacts/plate/perforated-plate.SLDPRT')
        request={'SchemaVersion':'1.0','Models':[{'Path':model,'Configuration':'NO-SUCH-CONFIGURATION'},{'Path':model}],
                 'Options':{'Template':str(ROOT/'artifacts/v1.2/template/company-a3.drwdot'),'OutputDirectory':str(root/'drawings')}}
        path=root/'request.json'; path.write_text(json.dumps(request),encoding='utf-8')
        result=subprocess.run([str(CLI),'generate','--request',str(path)],capture_output=True,encoding='utf-8',timeout=180)
        (root/'result.json').write_text(result.stdout,encoding='utf-8')
        self.assertEqual(result.returncode,4,result.stdout+result.stderr)
        data=json.loads(result.stdout)
        self.assertEqual((data['Planned'],data['Failed'],data['Completed']),(2,1,1))
        self.assertTrue(Path(data['Jobs'][1]['Drawing']).is_file())

    @unittest.skipUnless(NATIVE,'requires a running SolidWorks instance and pywin32')
    def test_native_busy_returns_code_five_without_generating(self):
        import win32com.client
        import win32event
        import win32api
        app=win32com.client.GetActiveObject('SldWorks.Application')
        member=app.GetProcessID
        pid=member() if callable(member) else member
        mutex=win32event.CreateMutex(None,False,'Local\\AutoDrawing.SolidWorks.'+str(pid))
        self.assertEqual(win32event.WaitForSingleObject(mutex,0),0)
        try:
            root=ROOT/'artifacts/v1.2/busy'; root.mkdir(parents=True,exist_ok=True)
            request={'SchemaVersion':'1.0','Models':[{'Path':str(ROOT/'artifacts/plate/perforated-plate.SLDPRT')}],
                     'Options':{'Template':str(ROOT/'artifacts/v1.2/template/company-a3.drwdot'),'OutputDirectory':str(root/'drawings')}}
            path=root/'request.json'; path.write_text(json.dumps(request),encoding='utf-8')
            result=subprocess.run([str(CLI),'generate','--request',str(path),'--connect-only'],capture_output=True,encoding='utf-8',timeout=20)
            self.assertEqual(result.returncode,5,result.stdout+result.stderr)
            data=json.loads(result.stdout); self.assertEqual(data['Status'],'busy'); self.assertEqual(data['Jobs'],[])
        finally:
            win32event.ReleaseMutex(mutex); win32api.CloseHandle(mutex)


if __name__=='__main__': unittest.main()
