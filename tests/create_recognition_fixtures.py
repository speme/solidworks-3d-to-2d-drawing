"""Owned native fixtures for geometry recognition regression. No existing model is changed."""
import sys
from pathlib import Path
sys.path.insert(0,str(Path.home()/'.codex/skills/solidworks-automation-skill/scripts'))
from sw_session import SolidWorksSession
from sw_connect import get_com_member as m
from sw_part import start_sketch,end_sketch,sketch_corner_rectangle,sketch_circle,sketch_slot,extrude_boss,cut_extrude

s=SolidWorksSession()
root=Path('artifacts/v1.1/fixtures').resolve(); root.mkdir(parents=True,exist_ok=True)

def save(doc,name):
    assert m(doc,'ForceRebuild3',False)
    assert len(m(doc,'GetBodies2',0,False))==1
    s.save(doc,str(root/(name+'.SLDPRT')))
    s.close(doc)

d=s.new_part(); start_sketch(d,'Front Plane'); sketch_circle(d,0,0,.008)
assert extrude_boss(d,end_sketch(d),.08) is not None
save(d,'shaft')

d=s.new_part(); start_sketch(d,'Top Plane'); sketch_corner_rectangle(d,0,0,.1,.06); sketch_circle(d,.025,.03,.004); sketch_circle(d,.075,.03,.004)
assert extrude_boss(d,end_sketch(d),.01) is not None
save(d,'top-holes')

d=s.new_part(); start_sketch(d,'Front Plane'); sketch_corner_rectangle(d,0,0,.1,.05); sketch_slot(d,.025,.025,.065,.025,.01)
assert extrude_boss(d,end_sketch(d),.01) is not None
save(d,'slot')

d=s.new_part(); start_sketch(d,'Front Plane'); sketch_corner_rectangle(d,0,0,.08,.05)
assert extrude_boss(d,end_sketch(d),.02) is not None
start_sketch(d,'Front Plane'); sketch_circle(d,.025,.025,.004)
assert cut_extrude(d,end_sketch(d),.012) is not None
save(d,'blind-hole')

d=s.new_part(); start_sketch(d,'Front Plane'); sketch_corner_rectangle(d,0,0,.08,.05); sketch_circle(d,.025,.025,.003)
assert extrude_boss(d,end_sketch(d),.02) is not None
start_sketch(d,'Front Plane'); sketch_circle(d,.025,.025,.006)
assert cut_extrude(d,end_sketch(d),.006) is not None
save(d,'stepped-bore')
print(root)
