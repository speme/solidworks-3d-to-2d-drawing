using System;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SwView=SolidWorks.Interop.sldworks.View;
class ProbeChamfers
{
    static string P(object value) { return value==null?"null":string.Join(",",((double[])value).Select(x=>(x*1000).ToString("0.#####"))); }
    static string Entity(object entity)
    {
        var edge=entity as Edge; if(edge!=null) return "edge["+(edge.GetStartVertex()==null?"circle":P(((Vertex)edge.GetStartVertex()).GetPoint())+" -> "+P(((Vertex)edge.GetEndVertex()).GetPoint()))+"]";
        var vertex=entity as Vertex; if(vertex!=null) return "vertex["+P(vertex.GetPoint())+"]";
        return entity==null?"null":entity.GetType().Name;
    }
    [STAThread] static void Main(string[] args)
    {
        Console.OutputEncoding=new System.Text.UTF8Encoding(false);
        var app=(SldWorks)Marshal.GetActiveObject("SldWorks.Application"); var previous=app.ActiveDoc as ModelDoc2; int error=0,warning=0;
        var doc=app.GetOpenDocumentByName(args[0]) as ModelDoc2; bool opened=doc==null;
        try {
            if(opened) doc=app.OpenDoc6(args[0],args[0].ToLowerInvariant().EndsWith("slddrw")?3:1,3,"",ref error,ref warning);
            if(doc.GetType()==1) {
                foreach(Body2 body in (object[])((PartDoc)doc).GetBodies2(0,false)) {
                    int i=0; foreach(Face2 face in (object[])body.GetFaces()) {
                        var surf=(Surface)face.GetSurface(); if(!surf.IsPlane()) { i++; continue; }
                        Console.WriteLine("FACE "+(i++)+" normal="+P(face.Normal)+" owner="+((Feature)face.GetFeature()).Name);
                        foreach(Edge edge in (object[])face.GetEdges()) Console.WriteLine("  "+Entity(edge));
                    }
                }
            } else {
                if(args.Length>1 && args[1]=="--leaders") {
                    var drawing=(DrawingDoc)doc;
                    for(var v=((SwView)drawing.GetFirstView()).GetNextView() as SwView;v!=null;v=v.GetNextView() as SwView)
                    for(var dd=v.GetFirstDisplayDimension5();dd!=null;dd=dd.GetNext5()) if(dd.Type2==10) {
                        var ann=(Annotation)dd.GetAnnotation(); Console.WriteLine("LEADER before="+ann.GetLeaderPerpendicular());
                        Console.WriteLine("SET "+ann.SetLeader3(ann.GetLeaderStyle(),ann.GetLeaderSide(),ann.GetSmartArrowHeadStyle(),true,ann.GetLeaderAllAround(),ann.GetDashedLeader())+" after="+ann.GetLeaderPerpendicular());
                    }
                    drawing.ActivateView(""); doc.ForceRebuild3(false);
                    var output=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(args[0]),"probe-leaders.slddrw");
                    doc.Extension.SaveAs(output,0,1,null,ref error,ref warning); doc.Extension.SaveAs(System.IO.Path.ChangeExtension(output,".pdf"),0,1,null,ref error,ref warning);
                }
                if(args.Length>1 && args[1]=="--add") {
                    var drawing=(DrawingDoc)doc; var view=((SwView)drawing.GetFirstView()).GetNextView() as SwView;
                    foreach(var component in view.GetVisibleComponents() as object[] ?? new object[0]) {
                        Console.WriteLine("VISIBLE "+((view.GetVisibleEntities2((Component2)component,1) as object[])??new object[0]).Length);
                        foreach(var ve in (view.GetVisibleEntities2((Component2)component,1) as object[])??new object[0]) {
                            var native=((PartDoc)view.ReferencedDocument).GetCorrespondingEntity(ve);
                            Console.WriteLine("VIS "+Entity(ve)+" id="+AutoDrawing.ModelRecognizer.Persist(view.ReferencedDocument,ve)+" native="+AutoDrawing.ModelRecognizer.Persist(view.ReferencedDocument,native));
                        }
                    }
                    var body=(Body2)((object[])((PartDoc)view.ReferencedDocument).GetBodies2(0,false))[0];
                    var edges=((object[])body.GetEdges()).Cast<Edge>().Where(x=>x.GetStartVertex()!=null && x.GetEndVertex()!=null).ToList();
                    Func<Edge,double,double,double,bool> starts=(edge,x,y,z)=> { var p=(double[])((Vertex)edge.GetStartVertex()).GetPoint(); return Math.Abs(p[0]-x)<1e-7 && Math.Abs(p[1]-y)<1e-7 && Math.Abs(p[2]-z)<1e-7; };
                    var bevel=edges.First(x=>starts(x,.067,0,.024) && Math.Abs(((double[])((Vertex)x.GetEndVertex()).GetPoint())[0]-.070)<1e-7);
                    var lead=edges.First(x=>starts(x,.070,.003,.024) && Math.Abs(((double[])((Vertex)x.GetEndVertex()).GetPoint())[1]-.150)<1e-7);
                    Console.WriteLine("BEVEL "+Entity(bevel)+" id="+AutoDrawing.ModelRecognizer.Persist(view.ReferencedDocument,bevel));
                    Console.WriteLine("LEAD "+Entity(lead)+" id="+AutoDrawing.ModelRecognizer.Persist(view.ReferencedDocument,lead));
                    doc.ClearSelection2(true); drawing.ActivateView(view.GetName2());
                    Console.WriteLine("SELECT "+view.SelectEntity(bevel,false)+" "+view.SelectEntity(lead,true));
                    var dd=drawing.AddChamferDim(.14,.16,0) as DisplayDimension;
                    Console.WriteLine("ADDED "+(dd!=null));
                    if(dd!=null) { double length=0,angle=0; Console.WriteLine("VALUES "+dd.GetDimension2(0).GetSystemChamferValues(ref length,ref angle)+" "+length+" / "+angle); dd.ChamferTextStyle=4; }
                    doc.ClearSelection2(true); drawing.ActivateView(""); doc.ForceRebuild3(false);
                    var output=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(args[0]),"probe-added.slddrw");
                    Console.WriteLine("SAVE "+doc.Extension.SaveAs(output,0,1,null,ref error,ref warning));
                    Console.WriteLine("PDF "+doc.Extension.SaveAs(System.IO.Path.ChangeExtension(output,".pdf"),0,1,null,ref error,ref warning));
                }
                for(var view=((SwView)((DrawingDoc)doc).GetFirstView()).GetNextView() as SwView;view!=null;view=view.GetNextView() as SwView)
                for(var dd=view.GetFirstDisplayDimension5();dd!=null;dd=dd.GetNext5()) {
                    var dim=dd.GetDimension2(0); var a=(Annotation)dd.GetAnnotation();
                    if(dd.Type2==10) for(int ix=0;ix<2;ix++) { var cd=dd.GetDimension2(ix); if(cd!=null) { double l=0,an=0; cd.GetSystemChamferValues(ref l,ref an); Console.WriteLine("CHAMFER "+ix+" name="+cd.FullName+" dimType="+cd.GetType()+" value="+cd.SystemValue+" length="+l+" angle="+an+" style="+dd.ChamferTextStyle); } }
                    Console.WriteLine(view.GetName2()+" "+dim.FullName+" value="+(dim.SystemValue*1000)+" type="+dd.Type2+" ref="+dd.IsReferenceDim()+" dir="+(dim.DimensionLineDirection==null?"null":P(dim.DimensionLineDirection.ArrayData))+" texts="+string.Join("|",Enumerable.Range(1,4).Select(ix=>dd.GetText(ix))));
                    foreach(var point in dim.ReferencePoints as object[] ?? new object[0]) Console.WriteLine("  RP "+P(((MathPoint)point).ArrayData));
                    foreach(var entity in a.GetAttachedEntities3() as object[] ?? new object[0]) { try { var native=((PartDoc)view.ReferencedDocument).GetCorrespondingEntity(entity); Console.WriteLine("  ATT "+Entity(entity)+" native="+AutoDrawing.ModelRecognizer.Persist(view.ReferencedDocument,native));} catch(Exception ex) {Console.WriteLine(ex.Message);} }
                }
            }
        } finally { if(opened && doc!=null) app.CloseDoc(doc.GetTitle()); if(previous!=null) app.ActivateDoc3(previous.GetTitle(),false,0,ref error); }
    }
}
