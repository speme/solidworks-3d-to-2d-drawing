using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
class ProbeThreadDrawing
{
    [STAThread]
    static void Main(string[] args)
    {
        Console.OutputEncoding=new System.Text.UTF8Encoding(false);
        var app=(SldWorks)Marshal.GetActiveObject("SldWorks.Application"); var previous=app.ActiveDoc as ModelDoc2;
        string target=Path.Combine(args[1],"thread-probe-"+Guid.NewGuid().ToString("N")+".SLDDRW"); Directory.CreateDirectory(args[1]); File.Copy(args[0],target);
        int e=0,w=0; var doc=app.OpenDoc6(target,3,1,"",ref e,ref w); var drawing=(DrawingDoc)doc;
        app.ActivateDoc3(doc.GetTitle(),false,0,ref e);
        Console.WriteLine("ACTIVE="+((ModelDoc2)app.ActiveDoc).GetTitle());
        try
        {
            doc.Extension.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplayCosmeticThreads,0,true);
            doc.Extension.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplayAnnotations,0,true);
            var baseView=((SolidWorks.Interop.sldworks.View)drawing.GetFirstView()).GetNextView() as SolidWorks.Interop.sldworks.View;
            var left=drawing.CreateDrawViewFromModelView3(baseView.GetReferencedModelName(),"*左视",.60,.40,0); doc.ForceRebuild3(false);
            drawing.ActivateView(left.GetName2()); doc.ClearSelection2(true); doc.Extension.SelectByID2(left.GetName2(),"DRAWINGVIEW",0,0,0,false,0,null,0);
            drawing.InsertModelAnnotations3(0,1,false,false,true,false); doc.ForceRebuild3(false);
            var sheet=(SolidWorks.Interop.sldworks.View)drawing.GetFirstView();
            for(var v=sheet.GetNextView() as SolidWorks.Interop.sldworks.View;v!=null;v=v.GetNextView() as SolidWorks.Interop.sldworks.View)
            {
                Console.WriteLine("VIEW "+v.GetName2()+" threads="+v.GetCThreadCount());
                for(var ct=v.GetFirstCThread() as CThread;ct!=null;ct=ct.GetNext() as CThread)
                {
                    var annotation=ct.GetAnnotation() as Annotation;
                    Console.WriteLine("CT note before="+(ct.ThreadCallout==null?"NULL":ct.ThreadCallout.GetText())+" visible="+annotation.Visible+" attached="+annotation.GetAttachedEntityCount3()+" dangling="+annotation.IsDangling());
                    foreach(var entity in (annotation.GetAttachedEntities3() as object[] ?? new object[0]))
                    {
                        Console.WriteLine("  attached edge="+(entity is Edge)+" feature="+(entity is Feature)+" face="+(entity is Face2));
                        var attachedFeature=entity as Feature;
                        if(attachedFeature!=null) { var data=attachedFeature.GetDefinition() as CosmeticThreadFeatureData; Console.WriteLine("  feature "+attachedFeature.Name+" type="+attachedFeature.GetTypeName2()+" data="+(data==null?"NULL":data.ThreadCallout)); }
                    }
                    drawing.ActivateView(v.GetName2()); doc.ClearSelection2(true);
                    var sd=((SelectionMgr)doc.SelectionManager).CreateSelectData(); sd.View=v;
                    Console.WriteLine("select="+annotation.Select3(false,sd));
                    Console.WriteLine("selected type="+((SelectionMgr)doc.SelectionManager).GetSelectedObjectType3(1,-1)+" name="+annotation.GetName());
                    drawing.InsertThreadCallout(); doc.ForceRebuild3(false);
                    Console.WriteLine("CT note after="+(ct.ThreadCallout==null?"NULL":ct.ThreadCallout.GetText()));
                    doc.ClearSelection2(true); annotation.Select3(false,null);
                    var sourceNote=doc.InsertNote("M10x1.5 INTERNAL; DEPTH 20 mm") as Note;
                    if(sourceNote!=null) { var na=sourceNote.GetAnnotation() as Annotation; Console.WriteLine("SOURCE NOTE attached="+sourceNote.IsAttached()+" count="+na.GetAttachedEntityCount3()+" dangling="+na.IsDangling()+" native="+(ct.ThreadCallout!=null)+" leaders="+sourceNote.GetLeaderCount()); Console.WriteLine("NOTE TYPES="+string.Join(",",(int[])na.GetAttachedEntityTypes())); Console.WriteLine("VIEW NOTES="+((v.GetNotes() as object[])??new object[0]).Length); }
                    doc.ClearSelection2(true); annotation.Select3(false,null);
                    var outline=(double[])v.GetOutline();
                    var dd=doc.AddDimension2(outline[2]+.025,outline[3]+.025,0) as DisplayDimension;
                    Console.WriteLine("CTDIM="+(dd==null?"NULL":dd.Type2+" value="+dd.GetDimension2(0).SystemValue+" hole="+dd.IsHoleCallout()));
                    if(dd!=null) { dd.SetText(0,"M10x1.5 INTERNAL; DEPTH 20 mm"); Console.WriteLine("CTDIMTEXT="+dd.GetText(1)); }
                    Console.WriteLine("selectByName="+doc.Extension.SelectByID2(annotation.GetName(),"CTHREAD",0,0,0,false,0,null,0));
                    drawing.InsertThreadCallout(); doc.ForceRebuild3(false);
                    Console.WriteLine("CT note afterByName="+(ct.ThreadCallout==null?"NULL":ct.ThreadCallout.GetText()));
                    foreach(var entity in (annotation.GetAttachedEntities3() as object[] ?? new object[0]))
                    {
                        var face=entity as Face2; if(face==null) continue;
                        foreach(Edge edge in (face.GetEdges() as object[] ?? new object[0]))
                        {
                            var curve=edge.GetCurve() as Curve; if(curve==null || !curve.IsCircle()) continue;
                            Console.WriteLine("circle="+string.Join(",",(double[])curve.CircleParams));
                            doc.ClearSelection2(true); bool selected=v.SelectEntity(edge,false); Console.WriteLine("edge select="+selected);
                            if(!selected) continue;
                            var bound=(double[])v.GetOutline(); var hole=drawing.AddHoleCallout2(bound[0]-.02,bound[3]+.02,0) as DisplayDimension;
                            Console.WriteLine("HOLE="+(hole==null?"NULL":hole.GetText(0)+" PREFIX="+hole.GetText(1)+" callout="+hole.IsHoleCallout()));
                            if(hole!=null) { Console.WriteLine("LINKED BEFORE="+hole.GetLinkedText()); Console.WriteLine("LINKED CODE="+hole.SetLinkedText("M10x1.5 INTERNAL; DEPTH 20 mm")); Console.WriteLine("LINKED AFTER="+hole.GetLinkedText()); }
                            if(hole!=null) break;
                        }
                    }
                    foreach(Annotation a in (doc.Extension.GetAnnotations() as object[] ?? new object[0])) { var n=a.GetSpecificAnnotation() as Note; if(n!=null) Console.WriteLine("GLOBAL NOTE "+n.GetText()); }
                }
                foreach(Annotation a in (v.GetAnnotations() as object[] ?? new object[0]))
                { var note=a.GetSpecificAnnotation() as Note; if(note!=null) Console.WriteLine("NOTE "+note.GetText()+" dangling="+a.IsDangling()); }
            }
            foreach(Annotation a in (doc.Extension.GetAnnotations() as object[] ?? new object[0])) { var n=a.GetSpecificAnnotation() as Note; if(n!=null) Console.WriteLine("GLOBAL NOTE "+n.GetText()); }
        }
        finally { app.CloseDoc(doc.GetTitle()); if(previous!=null) app.ActivateDoc3(previous.GetTitle(),false,0,ref e); }
    }
}
