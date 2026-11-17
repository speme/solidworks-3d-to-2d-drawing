import sys
from pathlib import Path
sys.path.insert(0,str(Path.home()/'.codex/skills/solidworks-automation-skill/scripts'))
from sw_session import SolidWorksSession
from sw_connect import get_com_member as m
from sw_part import start_sketch,end_sketch,sketch_corner_rectangle,extrude_boss
s=SolidWorksSession(); d=s.new_part()
start_sketch(d,'Front Plane'); sketch_corner_rectangle(d,0,0,.08,.05)
f=extrude_boss(d,end_sketch(d),.01); assert f is not None
original=m(m(m(d,'ConfigurationManager'),'ActiveConfiguration'),'Name')
assert m(d,'AddConfiguration3','Thick','','',0) is not None
if m(m(m(d,'ConfigurationManager'),'ActiveConfiguration'),'Name')!='Thick':
    assert m(d,'ShowConfiguration2','Thick')
dim=m(d,'Parameter','D1@'+m(f,'Name'))
# swSetValue_InThisConfiguration=1: do not accidentally update the default configuration.
status=m(dim,'SetSystemValue3',.02,1,None)
assert status==0, status
assert m(d,'ForceRebuild3',False)
assert m(d,'ShowConfiguration2',original)
assert abs(m(m(d,'Parameter','D1@'+m(f,'Name')),'SystemValue')-.01)<1e-9
p=Path('artifacts/v1.1/configurations/two-thickness.SLDPRT').resolve(); p.parent.mkdir(parents=True,exist_ok=True)
s.save(d,str(p)); s.close(d); print(p)
