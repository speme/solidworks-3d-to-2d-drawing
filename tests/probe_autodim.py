"""Isolated AutoDimension option matrix: only own disposable drawings are modified."""
import sys
from pathlib import Path
sys.path.insert(0,str(Path.home()/'.codex/skills/solidworks-automation-skill/scripts'))
from sw_session import SolidWorksSession
from sw_connect import get_com_member as m
import pythoncom
from win32com.client import VARIANT
s=SolidWorksSession()
model=s.open(str(Path('artifacts/plate/perforated-plate.SLDPRT').resolve()),read_only=True)
extents=[]
for entities,horizontal,vertical in [(1,-1,1),(0,1,1),(1,1,1),(0,-1,-1)]:
    doc=m(s.sw,'NewDocument',r'C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2026\templates\gb_a3.drwdot',0,0,0)
    view=m(doc,'CreateDrawViewFromModelView3',m(model,'GetPathName'),'*前视',.13,.19,0)
    m(doc,'ActivateView',m(view,'GetName2'))
    m(m(doc,'Extension'),'SelectByID2',m(view,'GetName2'),'DRAWINGVIEW',0.,0.,0.,False,0,VARIANT(pythoncom.VT_DISPATCH,None),0)
    status=m(doc,'AutoDimension',entities,1,horizontal,1,vertical)
    dd=m(view,'GetFirstDisplayDimension5'); count=0
    while dd is not None: count+=1; dd=m(dd,'GetNext5')
    extents.append({'entities':entities,'horizontal':horizontal,'vertical':vertical,'return':status,'count':count})
    s.close(doc)
print(extents)
s.close(model)
