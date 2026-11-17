"""Create an owned perforated plate fixture through the installed SolidWorks skill."""
import sys
from pathlib import Path
sys.path.insert(0, str(Path.home()/'.codex/skills/solidworks-automation-skill/scripts'))
from sw_session import SolidWorksSession
from sw_connect import get_com_member as m
from sw_part import start_sketch, end_sketch, sketch_corner_rectangle, sketch_circle, extrude_boss

s=SolidWorksSession()
part=s.new_part()
start_sketch(part,'Front Plane')
sketch_corner_rectangle(part,0,0,.1,.05)
sketch_circle(part,.025,.025,.003)
sketch_circle(part,.075,.025,.003)
sketch=end_sketch(part)
assert extrude_boss(part,sketch,.01) is not None
assert m(part,'ForceRebuild3',False)
bodies=m(part,'GetBodies2',0,False)
assert len(bodies)==1
faces=m(bodies[0],'GetFaces')
assert sum(bool(m(m(f,'GetSurface'),'IsCylinder')) for f in faces)==2
target=Path('artifacts/plate').resolve()
target.mkdir(parents=True,exist_ok=True)
s.save(part,str(target/'perforated-plate.SLDPRT'))
s.close(part)
print(target/'perforated-plate.SLDPRT')
