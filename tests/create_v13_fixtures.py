"""Owned native radius and gear drawing fixtures. Gear tooth roots are illustrative, not certified tooling geometry."""
import json
import sys
from pathlib import Path
sys.path.insert(0,str(Path.home()/'.codex/skills/solidworks-automation-skill/scripts'))
from sw_session import SolidWorksSession
from sw_connect import get_com_member as m
from sw_part import start_sketch,end_sketch,sketch_corner_rectangle,extrude_boss,fillet
from sw_gear import SpurGearSpec,create_external_spur_gear

root=Path('artifacts/v1.3.0/fixtures').resolve(); root.mkdir(parents=True,exist_ok=True)
s=SolidWorksSession()
d=s.new_part(); start_sketch(d,'Front Plane'); sketch_corner_rectangle(d,0,0,.1,.06)
assert extrude_boss(d,end_sketch(d),.01) is not None
targets=[]
for edge in m(m(d,'GetBodies2',0,False)[0],'GetEdges'):
    a=m(edge,'GetStartVertex'); b=m(edge,'GetEndVertex')
    if a is None or b is None: continue
    if all(abs(p[0]-.1)<1e-7 and abs(p[1]-.06)<1e-7 for p in (m(a,'GetPoint'),m(b,'GetPoint'))): targets.append(edge)
assert len(targets)==1
m(d,'ClearSelection2',True); assert m(targets[0],'Select2',False,0)
f=fillet(d,.003); assert f is not None
f.Name='Corner_R3'
assert m(d,'ForceRebuild3',False)
assert len(m(d,'GetBodies2',0,False))==1
s.save(d,str(root/'radius-3.SLDPRT')); s.close(d)

d=s.new_part()
f,evidence=create_external_spur_gear(d,SpurGearSpec(module_mm=2,teeth=24,face_width_mm=10,pressure_angle_deg=20,bore_diameter_mm=10,involute_segments=6,tip_arc_segments=4,root_arc_segments=3))
assert f is not None and m(d,'ForceRebuild3',False)
manager=m(m(d,'Extension'),'CustomPropertyManager','')
for key,value in {'Module':'2','Teeth':'24','PressureAngle':'20','HelixAngle':'0','GearType':'external_spur','GearGeometryLimit':'sampled involute with radial root transitions; engineering review required'}.items():
    assert m(manager,'Add3',key,30,value,1)==0
assert len(m(d,'GetBodies2',0,False))==1
s.save(d,str(root/'spur-gear-m2-z24.SLDPRT')); s.close(d)
(root/'gear-fixture-evidence.json').write_text(json.dumps(evidence,ensure_ascii=False,indent=2),encoding='utf-8')
print(root)
