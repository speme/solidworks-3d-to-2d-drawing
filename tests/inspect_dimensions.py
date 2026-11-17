"""Read-only diagnostics for reference geometry, using an already open drawing."""
import os
import sys
sys.path.insert(0, os.environ["SOLIDWORKS_AUTOMATION_SCRIPTS"])
from sw_session import SolidWorksSession
from sw_connect import get_com_member as m
s=SolidWorksSession()
d=m(s.sw,'ActiveDoc')
v=m(m(d,'GetFirstView'),'GetNextView')
while v is not None:
    dd=m(v,'GetFirstDisplayDimension5')
    while dd is not None:
        dim=m(dd,'GetDimension2',0)
        print(m(v,'GetName2'),m(dim,'FullName'),[m(p,'ArrayData') for p in m(dim,'ReferencePoints')],m(m(dim,'DimensionLineDirection'),'ArrayData'))
        dd=m(dd,'GetNext5')
    v=m(v,'GetNextView')
