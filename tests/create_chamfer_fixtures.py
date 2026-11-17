"""Owned profile/native chamfer fixtures; never modifies an existing source part."""
import math
import sys
from pathlib import Path
sys.path.insert(0,str(Path.home()/'.codex/skills/solidworks-automation-skill/scripts'))
from sw_session import SolidWorksSession
from sw_connect import get_com_member as m
from sw_part import start_sketch,end_sketch,sketch_line,sketch_circle,sketch_corner_rectangle,extrude_boss,chamfer

s=SolidWorksSession()
root=Path('artifacts/v1.2.5/fixtures').resolve(); root.mkdir(parents=True,exist_ok=True)

def save(doc,name):
    assert m(doc,'ForceRebuild3',False)
    assert len(m(doc,'GetBodies2',0,False))==1
    s.save(doc,str(root/(name+'.SLDPRT'))); s.close(doc)

d=s.new_part(); start_sketch(d,'Front Plane')
points=[(0,0),(.1,0),(.1,.056),(.1-.004/math.sqrt(3),.06),(0,.06)]
for a,b in zip(points,points[1:]+points[:1]): sketch_line(d,*a,*b)
sketch_circle(d,.003,.02,.001); sketch_circle(d,.03,.02,.001)
assert extrude_boss(d,end_sketch(d),.01) is not None
save(d,'profile-30')

d=s.new_part(); start_sketch(d,'Front Plane'); sketch_corner_rectangle(d,0,0,.1,.06)
assert extrude_boss(d,end_sketch(d),.01) is not None
edges=m(m(d,'GetBodies2',0,False)[0],'GetEdges')
targets=[]
for edge in edges:
    a=m(edge,'GetStartVertex'); b=m(edge,'GetEndVertex')
    if a is None or b is None: continue
    pa=m(a,'GetPoint'); pb=m(b,'GetPoint')
    if all(abs(p[0]-.1)<1e-7 and abs(p[1]-.06)<1e-7 for p in (pa,pb)):
        targets.append(edge)
assert len(targets)==1
m(d,'ClearSelection2',True); assert m(targets[0],'Select2',False,0)
assert chamfer(d,.003,45,tangent_propagation=False) is not None
save(d,'native-45')

d=s.new_part(); start_sketch(d,'Front Plane'); sketch_circle(d,0,0,.01)
assert extrude_boss(d,end_sketch(d),.05) is not None
targets=[]
for edge in m(m(d,'GetBodies2',0,False)[0],'GetEdges'):
    curve=m(edge,'GetCurve')
    if m(curve,'IsCircle') and abs(abs(m(curve,'CircleParams')[2])-.05)<1e-7: targets.append(edge)
assert len(targets)==1
m(d,'ClearSelection2',True); assert m(targets[0],'Select2',False,0)
assert chamfer(d,.001,45,tangent_propagation=False) is not None
save(d,'round-45')
print(root)
