using System;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SwView=SolidWorks.Interop.sldworks.View;
class InspectGraphics
{
    [STAThread] static void Main(string[] args)
    {
        var app=(SldWorks)Marshal.GetActiveObject("SldWorks.Application"); var prev=app.ActiveDoc as ModelDoc2; int e=0,w=0;
        var doc=app.GetOpenDocumentByName(args[0]) as ModelDoc2; bool opened=doc==null;
        try {
            if(opened) doc=app.OpenDoc6(args[0],3,3,"",ref e,ref w);
            for(var v=((SwView)((DrawingDoc)doc).GetFirstView()).GetNextView() as SwView;v!=null;v=v.GetNextView() as SwView) {
                Console.WriteLine("VIEW "+v.GetName2()+" "+string.Join(",",(double[])v.GetOutline()));
                Console.WriteLine("TRANS "+string.Join(",",(double[])v.ModelToViewTransform.ArrayData));
                int count=0;
                foreach(Component2 c in v.GetVisibleComponents() as object[] ?? new object[0]) foreach(Edge edge in v.GetVisibleEntities2(c,1) as object[] ?? new object[0]) if(((Curve)edge.GetCurve()).IsLine() && count++<2) {
                    var p=(CurveParamData)edge.GetCurveParams3(); Console.WriteLine("EDGE "+string.Join(",",(double[])p.StartPoint)+" -> "+string.Join(",",(double[])p.EndPoint)+" EV="+string.Join(",",((double[])((Curve)edge.GetCurve()).Evaluate2(p.UMinValue,0)).Take(3)));
                }
                for(var dd=v.GetFirstDisplayDimension5();dd!=null;dd=dd.GetNext5()) {
                    var data=(DisplayData)dd.GetDisplayData(); Console.WriteLine("DIM "+dd.Type2+" "+dd.GetDimension2(0).FullName+" lines="+data.GetLineCount()+" polys="+data.GetPolyLineCount());
                    for(int i=0;i<Math.Min(7,data.GetLineCount());i++) Console.WriteLine("L "+string.Join(",",(double[])data.GetLineAtIndex3(i)));
                }
                object entities; var raw=v.GetPolylines7(1,out entities); Console.WriteLine("RAW "+(raw==null?"null":raw.GetType().FullName)); var polys=raw as double[];
                Console.WriteLine("POLY "+(polys==null?"null":polys.Length+" "+string.Join(",",polys.Take(90))));
            }
        } finally { if(opened && doc!=null) app.CloseDoc(doc.GetTitle()); if(prev!=null) app.ActivateDoc3(prev.GetTitle(),false,0,ref e); }
    }
}
