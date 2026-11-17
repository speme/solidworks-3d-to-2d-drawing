import sys
from pathlib import Path
sys.path.insert(0,str(Path.home()/'.codex/skills/solidworks-automation-skill/scripts'))
from sw_session import SolidWorksSession
from sw_connect import get_com_member as m
s=SolidWorksSession()
d=s.open(str(Path(sys.argv[1]).resolve()),read_only=True)
v=m(m(d,'GetFirstView'),'GetNextView')
while v is not None:
    print(m(v,'GetName2'),m(v,'Position'),m(v,'GetOutline'),m(v,'ScaleDecimal'),m(v,'UseParentScale'),m(v,'UseSheetScale'))
    v=m(v,'GetNextView')
s.close(d)
