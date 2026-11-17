using System;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
class ProbeConicalChamfer
{
    [STAThread] static void Main(string[] args)
    {
        Console.OutputEncoding=new System.Text.UTF8Encoding(false);
        var app=(SldWorks)Marshal.GetActiveObject("SldWorks.Application"); var previous=app.ActiveDoc as ModelDoc2;
        int error=0,warning=0; ModelDoc2 model=null,doc=null; bool opened=false;
        try {
            model=app.GetOpenDocumentByName(args[0]) as ModelDoc2; opened=model==null;
            if(opened) model=app.OpenDoc6(args[0],1,3,"",ref error,ref warning);
            doc=(ModelDoc2)app.NewDocument(@"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2026\templates\gb_a3.drwdot",0,0,0);
            var drawing=(DrawingDoc)doc;
            var names=(string[])model.GetModelViewNames(); var name=names.Contains("*Right")?"*Right":"*右视";
            var view=drawing.CreateDrawViewFromModelView3(args[0],name,.18,.2,0);
            doc.ForceRebuild3(false);
            foreach(Component2 component in view.GetVisibleComponents() as object[] ?? new object[0]) {
                var silhouettes=(view.GetVisibleEntities2(component,4) as object[] ?? new object[0]).Cast<SilhouetteEdge>().ToList();
                foreach(var edge in silhouettes) {
                    var face=edge.GetFace(); var surf=(Surface)face.GetSurface();
                    Console.WriteLine("SIL cone="+surf.IsCone()+" cylinder="+surf.IsCylinder()+" line="+((Curve)edge.GetCurve()).IsLine()+" a="+string.Join(",",(double[])edge.GetStartPoint().ArrayData)+" b="+string.Join(",",(double[])edge.GetEndPoint().ArrayData));
                }
                foreach(var bevel in silhouettes.Where(e=>((Surface)e.GetFace().GetSurface()).IsCone()))
                foreach(var lead in silhouettes.Where(e=>((Surface)e.GetFace().GetSurface()).IsCylinder())) {
                    var bpts=new[]{(double[])bevel.GetStartPoint().ArrayData,(double[])bevel.GetEndPoint().ArrayData}; var lpts=new[]{(double[])lead.GetStartPoint().ArrayData,(double[])lead.GetEndPoint().ArrayData};
                    if(!bpts.Any(p=>lpts.Any(q=>p.Zip(q,(x,y)=>Math.Abs(x-y)).Max()<1e-7))) continue;
                    doc.ClearSelection2(true); drawing.ActivateView(view.GetName2()); var data=((SelectionMgr)doc.SelectionManager).CreateSelectData(); data.View=view;
                    Console.WriteLine("SELECT "+bevel.Select2(false,data)+" "+lead.Select2(true,data));
                    DisplayDimension dd=null; try { dd=drawing.AddChamferDim(.21,.24,0) as DisplayDimension; } catch(COMException ex) { Console.WriteLine("COM "+ex.ErrorCode); }
                    if(dd==null) continue;
                    double l=0,a=0; dd.GetDimension2(0).GetSystemChamferValues(ref l,ref a); Console.WriteLine("ADDED "+l+" / "+a);
                    dd.ChamferTextStyle=4; doc.ForceRebuild3(false);
                    foreach(var entity in ((Annotation)dd.GetAnnotation()).GetAttachedEntities3() as object[] ?? new object[0]) Console.WriteLine("ATT silhouette="+(entity is SilhouetteEdge)+" edge="+(entity is Edge)+" face="+(entity is Face2));
                    goto Saved;
                }
            }
            Saved: doc.ClearSelection2(true); drawing.ActivateView(""); doc.ForceRebuild3(false);
            doc.Extension.SaveAs(args[1],0,1,null,ref error,ref warning);
            doc.Extension.SaveAs(System.IO.Path.ChangeExtension(args[1],".pdf"),0,1,null,ref error,ref warning);
        } finally { if(doc!=null) app.CloseDoc(doc.GetTitle()); if(opened && model!=null) app.CloseDoc(model.GetTitle()); if(previous!=null) app.ActivateDoc3(previous.GetTitle(),false,0,ref error); }
    }
}
