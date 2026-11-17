"""Create owned native templates with a linked title property for external-interface regression."""
import sys
from pathlib import Path
import shutil
sys.path.insert(0,str(Path.home()/'.codex/skills/solidworks-automation-skill/scripts'))
from sw_session import SolidWorksSession
from sw_connect import get_com_member as m
s=SolidWorksSession(); previous=m(s.sw,'ActiveDoc')
root=Path('artifacts/v1.2/template').resolve(); root.mkdir(parents=True,exist_ok=True)
d=m(s.sw,'NewDocument',r'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2026\templates\gb_a3.drwdot',0,0,0)
assert d is not None
sheet=m(d,'GetCurrentSheet'); source=m(sheet,'GetTemplateName')
source_path=Path(source)
if not source_path.is_file():
    source_path=Path(r'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2026\lang\Chinese-Simplified\sheetformat\a3 - gb.slddrt')
assert source_path.is_file(), source
shutil.copyfile(source_path,root/'company-a3.slddrt')
props=m(m(d,'Extension'),'CustomPropertyManager','')
assert m(props,'Add3','DrawingNo',30,'TEMPLATE-DEFAULT',1)==0
note=m(d,'InsertNote','$PRP:"DrawingNo"')
assert note is not None
annotation=m(note,'GetAnnotation')
assert m(annotation,'SetPosition2',.12,.032,0.)
s.save(d,str(root/'company-a3.drwdot'))
s.close(d)
print(root)
