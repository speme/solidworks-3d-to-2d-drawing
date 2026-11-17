using System;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SwView=SolidWorks.Interop.sldworks.View;
class ProbeDimensionSources
{
    static string P(object a) { return a is double[]?string.Join(",",(double[])a):"null"; }
    [STAThread] static void Main(string[] args) {
        var app=(SldWorks)Marshal.GetActiveObject("SldWorks.Application"); var prev=app.ActiveDoc as ModelDoc2; int e=0,w=0;
        var doc=app.GetOpenDocumentByName(args[0]) as ModelDoc2; bool opened=doc==null;
        try {
            if(opened) doc=app.OpenDoc6(args[0],3,3,"",ref e,ref w);
            for(var v=((SwView)((DrawingDoc)doc).GetFirstView()).GetNextView() as SwView;v!=null;v=v.GetNextView() as SwView)
            for(var dd=v.GetFirstDisplayDimension5();dd!=null;dd=dd.GetNext5()) {
                if(args.Length>1 && dd.Type2!=5) continue;
                if(dd.Type2==5) Console.WriteLine("TEXTS "+string.Join(" | ",Enumerable.Range(1,4).Select(i=>"["+dd.GetText(i)+"]")));
                var d=dd.GetDimension2(0); Console.WriteLine("DIM "+v.GetName2()+" "+d.FullName.Split('@')[0]+" t="+dd.Type2+" val="+d.SystemValue+" dir="+(d.DimensionLineDirection==null?"null":P(d.DimensionLineDirection.ArrayData)));
                Console.WriteLine("PTS "+string.Join(" / ",(d.ReferencePoints as object[] ?? new object[0]).Cast<MathPoint>().Select(x=>P(x.ArrayData))));
                foreach(var a in ((Annotation)dd.GetAnnotation()).GetAttachedEntities3() as object[] ?? new object[0]) {
                    var native=((PartDoc)v.ReferencedDocument).GetCorrespondingEntity(a); var edge=native as Edge;
                    Console.WriteLine("ENTITY "+(native is Vertex?"vertex "+P(((Vertex)native).GetPoint()):edge!=null?"edge":"other"));
                    if(edge==null) continue; var c=edge.GetCurve() as Curve;
                    Console.WriteLine(" CURVE "+(c==null?"null":c.IsCircle()?"circle "+P(c.CircleParams):c.IsLine()?"line "+P(c.LineParams):"other"));
                    foreach(Face2 f in edge.GetTwoAdjacentFaces2() as object[] ?? new object[0]) { var s=f.GetSurface() as Surface; Console.WriteLine(" FACE "+(s.IsPlane()?"plane "+P(s.PlaneParams):s.IsCylinder()?"cylinder "+P(s.CylinderParams):"other")); }
                }
            }
        } finally { if(opened && doc!=null) app.CloseDoc(doc.GetTitle()); if(prev!=null) app.ActivateDoc3(prev.GetTitle(),false,0,ref e); }
    }
}
