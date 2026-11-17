using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SwView=SolidWorks.Interop.sldworks.View;

namespace AutoDrawing
{
    public sealed class ChamferCalloutEvidence
    {
        public string Face { get; set; }
        public string Owner { get; set; }
        public string View { get; set; }
        public string DimensionName { get; set; }
        public double DistanceMm { get; set; }
        public double AngleDegrees { get; set; }
        public int TextStyle { get; set; }
        public string Text { get; set; }
        public double[] Position { get; set; }
        public List<string> Entities { get; set; }
        public bool Reopened { get; set; }
    }
    public sealed class RemovedChamferDimension
    {
        public string View { get; set; }
        public string DimensionName { get; set; }
        public double ValueMm { get; set; }
        public List<string> ChamferFaces { get; set; }
    }
    public sealed class SkippedChamfer
    {
        public string Face { get; set; }
        public string Owner { get; set; }
        public string SourceFeatureType { get; set; }
        public string Reason { get; set; }
        public double AxialSetbackMm { get; set; }
        public double RadialSetbackMm { get; set; }
    }
    public static class ChamferRules
    {
        // Never enter the host silhouette API for imported/geometry-only cones.
        // Checking a returned curve is too late: the scan itself can block.
        public static bool CanProjectCone(string sourceFeatureType,bool trackedNativeChamfer)
        { return sourceFeatureType=="Chamfer" && trackedNativeChamfer; }
    }
    internal static class ChamferAnnotations
    {
        // Recognize a rectangular bevel between two perpendicular support planes. This also works
        // for bevels drawn in the original extrusion sketch, with no separate Chamfer feature.
        private sealed class Section
        {
            public Edge Edge,Lead;
            public SilhouetteEdge Silhouette,SilhouetteLead;
            public double[] A,B,Corner,Normal;
        }
        private sealed class Bevel
        {
            public Body2 Body;
            public Face2 Cone;
            public double[] Axis;
            public HashSet<string> ConeCircles;
            public string CapCircle;
            public string CylinderFace;
            public double CapRadius,AxialSetback,RadialSetback;
            public string Id,Owner,SourceFeatureType;
            public List<Section> Sections=new List<Section>();
            public HashSet<string> Vertices=new HashSet<string>();
            public bool Annotated;
        }
        private static double[] Sub(double[] a,double[] b) { return a.Zip(b,(x,y)=>x-y).ToArray(); }
        private static double Dot(double[] a,double[] b) { return a.Zip(b,(x,y)=>x*y).Sum(); }
        private static double Norm(double[] a) { return Math.Sqrt(Dot(a,a)); }
        private static double[] Cross(double[] a,double[] b) { return new[]{a[1]*b[2]-a[2]*b[1],a[2]*b[0]-a[0]*b[2],a[0]*b[1]-a[1]*b[0]}; }
        private static double[] Unit(double[] a) { double n=Norm(a); return a.Select(x=>x/n).ToArray(); }
        private static bool Near(double[] a,double[] b) { return Norm(Sub(a,b))<1e-7; }
        private static double[] Point(Vertex v) { return v==null?null:v.GetPoint() as double[]; }
        private static bool Line(Edge e) { var c=e.GetCurve() as Curve; return c!=null && c.IsLine() && e.GetStartVertex()!=null && e.GetEndVertex()!=null; }
        private static Face2 Other(ModelDoc2 model,Edge edge,Face2 face)
        { string id=ModelRecognizer.Persist(model,face); return ((edge.GetTwoAdjacentFaces2() as object[])??new object[0]).Cast<Face2>().FirstOrDefault(f=>ModelRecognizer.Persist(model,f)!=id); }
        private static double[] Normal(Face2 face)
        { return face!=null && ((Surface)face.GetSurface()).IsPlane()?face.Normal as double[]:null; }
        private static string VertexId(ModelDoc2 model,Vertex vertex) { return ModelRecognizer.Persist(model,vertex); }
        private static IEnumerable<Edge> Leads(ModelDoc2 model,Vertex vertex,Face2 cap,Edge bevel)
        {
            string capId=ModelRecognizer.Persist(model,cap),edgeId=ModelRecognizer.Persist(model,bevel);
            return (vertex.GetEdges() as object[] ?? new object[0]).Cast<Edge>().Where(e=>Line(e) && ModelRecognizer.Persist(model,e)!=edgeId && (e.GetTwoAdjacentFaces2() as object[] ?? new object[0]).Cast<Face2>().Any(f=>ModelRecognizer.Persist(model,f)==capId));
        }
        private static List<Bevel> Find(ModelDoc2 model,Action<string> progress)
        {
            var result=new List<Bevel>();
            if(model.GetType()!=1) return result;
            var faces=((PartDoc)model).GetBodies2(0,false) as object[] ?? new object[0];
            int scanned=0;
            foreach(Body2 body in faces)
            foreach(Face2 face in body.GetFaces() as object[] ?? new object[0])
            {
                if(scanned++%25==0) progress("倒角识别：已扫描 "+scanned+" 个面");
                var surface=(Surface)face.GetSurface();
                if(surface.IsCone()) { var cone=Conical(model,body,face); if(cone!=null) result.Add(cone); continue; }
                var normal=Normal(face); var edges=face.GetEdges() as object[];
                if(normal==null || edges==null || edges.Length!=4 || edges.Cast<Edge>().Any(e=>!Line(e))) continue;
                var bevel=new Bevel { Body=body,Id=ModelRecognizer.Persist(model,face),Owner=((Feature)face.GetFeature()).Name };
                foreach(Edge edge in edges)
                {
                    var cap=Other(model,edge,face); var capNormal=Normal(cap);
                    if(capNormal==null || Math.Abs(Dot(normal,capNormal))>1e-7) continue;
                    var va=(Vertex)edge.GetStartVertex(); var vb=(Vertex)edge.GetEndVertex(); var a=Point(va); var b=Point(vb);
                    var leadsA=Leads(model,va,cap,edge).ToList(); var leadsB=Leads(model,vb,cap,edge).ToList();
                    foreach(var leadA in leadsA)
                    foreach(var leadB in leadsB)
                    {
                        var supportA=Other(model,leadA,cap); var supportB=Other(model,leadB,cap);
                        var na=Normal(supportA); var nb=Normal(supportB);
                        if(na==null || nb==null || Math.Abs(Dot(na,nb))>1e-7 || Math.Abs(Dot(na,capNormal))>1e-7 || Math.Abs(Dot(nb,capNormal))>1e-7) continue;
                        var da=Sub(Point((Vertex)leadA.GetEndVertex()),Point((Vertex)leadA.GetStartVertex()));
                        var db=Sub(Point((Vertex)leadB.GetEndVertex()),Point((Vertex)leadB.GetStartVertex()));
                        var u=Unit(da); var v=Unit(db);
                        if(Math.Abs(Dot(u,v))>1e-7) continue;
                        double t=Dot(Sub(b,a),u); var corner=a.Zip(u,(x,y)=>x+t*y).ToArray();
                        if(Norm(Sub(Sub(corner,b),v.Select(x=>x*Dot(Sub(corner,b),v)).ToArray()))>1e-7) continue;
                        double sa=Norm(Sub(corner,a)),sb=Norm(Sub(corner,b));
                        if(sa<1e-6 || sb<1e-6 || Dot(normal,Sub(corner,a))<1e-7) continue; // convex removed corner
                        // Geometry-only recognition is deliberately bounded; broad sloped walls are not assumed to be edge breaks.
                        bool native=((Feature)face.GetFeature()).GetTypeName2()=="Chamfer";
                        if(!native && (sa>Norm(da)*.5 || sb>Norm(db)*.5)) continue;
                        bevel.Sections.Add(new Section { Edge=edge,Lead=leadA,A=a,B=b,Corner=corner,Normal=capNormal });
                        bevel.Vertices.Add(VertexId(model,va)); bevel.Vertices.Add(VertexId(model,vb));
                    }
                }
                if(bevel.Sections.Count==2 && bevel.Id!="" && !bevel.Vertices.Contains("")) result.Add(bevel);
            }
            return result;
        }
        private static Bevel Conical(ModelDoc2 model,Body2 body,Face2 face)
        {
            var source=face.GetFeature() as Feature;
            string sourceType=source==null?"":source.GetTypeName2();
            var circles=((object[])face.GetEdges()).Cast<Edge>().Where(e=>((Curve)e.GetCurve()).IsCircle()).ToList();
            if(circles.Count!=2 || sourceType=="HoleWzd") return null;
            var a=(double[])((Curve)circles[0].GetCurve()).CircleParams; var b=(double[])((Curve)circles[1].GetCurve()).CircleParams;
            var axis=Unit(a.Skip(3).Take(3).ToArray()); var center=Sub(a.Take(3).ToArray(),b.Take(3).ToArray());
            double axial=Math.Abs(Dot(center,axis)),radial=Math.Abs(a[6]-b[6]);
            if(axial<1e-6 || radial<1e-6 || Norm(Sub(center,axis.Select(x=>x*Dot(center,axis)).ToArray()))>1e-7) return null;
            var neighbors=circles.Select(e=>Other(model,e,face)).Where(f=>f!=null).ToList();
            var cylinder=neighbors.FirstOrDefault(f=>((Surface)f.GetSurface()).IsCylinder());
            var plane=neighbors.FirstOrDefault(f=>Normal(f)!=null && Math.Abs(Dot(Normal(f),axis))>.999999);
            if(cylinder==null || plane==null) return null;
            bool native=sourceType=="Chamfer";
            var cp=(double[])((Surface)cylinder.GetSurface()).CylinderParams;
            if(!native && (axial>cp[6]*.5 || radial>cp[6]*.5)) return null;
            var cap=circles.First(e=>Normal(Other(model,e,face))!=null);
            return new Bevel { Body=body,Cone=face,Axis=axis,Id=ModelRecognizer.Persist(model,face),Owner=source==null?"":source.Name,SourceFeatureType=sourceType,CylinderFace=ModelRecognizer.Persist(model,cylinder),ConeCircles=new HashSet<string>(circles.Select(e=>ModelRecognizer.Persist(model,e))),CapCircle=ModelRecognizer.Persist(model,cap),CapRadius=((double[])((Curve)cap.GetCurve()).CircleParams)[6],AxialSetback=axial,RadialSetback=radial };
        }
        private static bool StraightSilhouette(SilhouetteEdge edge)
        {
            if(edge==null) return false;
            var curve=edge.GetCurve() as Curve;
            if(curve!=null) return curve.IsLine();
            var sourceFace=edge.GetFace(); var owner=sourceFace==null?null:sourceFace.GetFeature() as Feature;
            // Analytic endpoints alone do not make an imported, curve-less silhouette selectable.
            // Select2 can wait indefinitely on such wrappers; leave these candidates for review.
            if(owner!=null && RecognitionRules.Category(owner.GetTypeName2())=="imported") return false;
            // Imported analytic faces can expose a valid silhouette without an ICurve.
            var face=sourceFace; var surface=face==null?null:face.GetSurface() as Surface;
            if(surface==null || (!surface.IsCylinder() && !surface.IsCone())) return false;
            var parameters=(double[])(surface.IsCylinder()?surface.CylinderParams:surface.ConeParams);
            var view=edge.GetView(); if(view==null) return false;
            var t=(double[])view.ModelToViewTransform.ArrayData;
            if(Math.Abs(parameters[3]*t[2]+parameters[4]*t[5]+parameters[5]*t[8])>1e-7) return false;
            var a=edge.GetStartPoint(); var b=edge.GetEndPoint();
            return a!=null && b!=null && Norm(Sub((double[])a.ArrayData,(double[])b.ArrayData))>1e-8;
        }
        private static IEnumerable<Section> ConeSections(ModelDoc2 model,SwView view,Bevel bevel,Dictionary<string,Dictionary<string,List<SilhouetteEdge>>> cache,JobReport report,Action<string> progress)
        {
            progress("原生倒角轮廓组件读取："+view.GetName2());
            foreach(Component2 component in view.GetVisibleComponents() as object[] ?? new object[0])
            {
                string key=view.GetName2()+"|"+component.Name2;
                Dictionary<string,List<SilhouetteEdge>> byFace;
                if(!cache.TryGetValue(key,out byFace)) {
                    report.ChamferSilhouetteScans++;
                    progress("原生倒角轮廓扫描开始："+view.GetName2());
                    var edges=(view.GetVisibleEntities2(component,4) as object[] ?? new object[0]).OfType<SilhouetteEdge>().Where(StraightSilhouette).ToList();
                    byFace=edges.GroupBy(e=>ModelRecognizer.PersistFromView(model,e.GetFace())).ToDictionary(g=>g.Key,g=>g.ToList());
                    cache[key]=byFace;
                    progress("原生倒角轮廓扫描完成："+view.GetName2()+"，"+edges.Count+" 条");
                }
                List<SilhouetteEdge> coneEdges,cylinderEdges;
                if(!byFace.TryGetValue(bevel.Id,out coneEdges) || !byFace.TryGetValue(bevel.CylinderFace,out cylinderEdges)) continue;
                foreach(var edge in coneEdges)
                foreach(var lead in cylinderEdges)
                {
                    var a=(double[])edge.GetStartPoint().ArrayData; var b=(double[])edge.GetEndPoint().ArrayData;
                    var c=(double[])lead.GetStartPoint().ArrayData; var d=(double[])lead.GetEndPoint().ArrayData;
                    double[] shared=Near(a,c)||Near(a,d)?a:Near(b,c)||Near(b,d)?b:null;
                    if(shared==null) continue;
                    var u=Unit(Sub(c,d));
                    if(Math.Abs(Dot(u,bevel.Axis))<.999999) continue;
                    var other=Near(a,shared)?b:a;
                    var corner=shared.Zip(u,(x,y)=>x+Dot(Sub(other,shared),u)*y).ToArray();
                    var normal=Unit(Cross(Sub(a,b),u));
                    yield return new Section { Silhouette=edge,SilhouetteLead=lead,A=a,B=b,Corner=corner,Normal=normal };
                }
            }
        }
        private static IEnumerable<SwView> Views(DrawingDoc drawing)
        { for(var v=((SwView)drawing.GetFirstView()).GetNextView() as SwView;v!=null;v=v.GetNextView() as SwView) yield return v; }
        private static HashSet<string> Visible(ModelDoc2 model,SwView view,Action<string> progress)
        {
            var result=new HashSet<string>();
            int count=0; progress("倒角可见边检查："+view.GetName2());
            foreach(Component2 component in view.GetVisibleComponents() as object[] ?? new object[0])
            foreach(var edge in view.GetVisibleEntities2(component,1) as object[] ?? new object[0]) { if(count++%25==0) progress("倒角可见边："+view.GetName2()+"，"+count+" 条"); result.Add(ModelRecognizer.PersistFromView(model,edge)); }
            return result;
        }
        private static IEnumerable<DisplayDimension> Dimensions(SwView view)
        { for(var d=view.GetFirstDisplayDimension5();d!=null;d=d.GetNext5()) yield return d; }
        private static string Name(DisplayDimension d) { return d.GetDimension2(0).FullName.Split('@')[0]; }
        private static List<string> Entities(ModelDoc2 model,DisplayDimension d)
        { return (((Annotation)d.GetAnnotation()).GetAttachedEntities3() as object[] ?? new object[0]).Select(e=>ModelRecognizer.PersistFromView(model,e)).Where(s=>s!="").ToList(); }
        internal static string Text(DisplayDimension d)
        { var data=(DisplayData)d.GetDisplayData(); return data==null?"":string.Join(" ",Enumerable.Range(0,data.GetTextCount()).Select(i=>data.GetTextAtIndex(i))); }
        internal static bool Values(DisplayDimension display,out double length,out double angle)
        {
            length=0; angle=0; var dimension=display.GetDimension2(0);
            if(!dimension.GetSystemChamferValues(ref length,ref angle)) return false;
            // SW2026 can reorder chamfer parameters when the native text style changes.
            // Resolve the pair against both actual selected edges, rather than guessing
            // from magnitudes or trusting the order of GetDimension2(0).
            var edges=(((Annotation)display.GetAnnotation()).GetAttachedEntities3() as object[] ?? new object[0]);
            double[] a,b,c,d;
            if(edges.Length==2 && Segment(edges[0],out a,out b) && Segment(edges[1],out c,out d)) {
                if(!Near(a,c) && !Near(a,d) && !Near(b,c) && !Near(b,d)) return false;
                double theta=Math.Acos(Math.Min(1,Math.Abs(Dot(Unit(Sub(a,b)),Unit(Sub(c,d))))));
                var legs=new[]{Norm(Sub(a,b))*Math.Cos(theta),Norm(Sub(a,b))*Math.Sin(theta)};
                Func<double,double,bool> fits=(l,t)=>legs.Any(v=>Math.Abs(l-v)<1e-7) && (Math.Abs(t-theta)<1e-6 || Math.Abs(t-(Math.PI/2-theta))<1e-6);
                if(!fits(length,angle)) {
                    if(!fits(angle,length)) return false;
                    double value=length; length=angle; angle=value;
                }
            }
            return length>0 && angle>0 && angle<Math.PI/2;
        }
        private static bool Segment(object entity,out double[] a,out double[] b)
        {
            a=null; b=null; var edge=entity as Edge;
            if(edge!=null && Line(edge)) { a=Point((Vertex)edge.GetStartVertex()); b=Point((Vertex)edge.GetEndVertex()); return true; }
            var silhouette=entity as SilhouetteEdge;
            if(silhouette!=null && StraightSilhouette(silhouette)) { a=(double[])silhouette.GetStartPoint().ArrayData; b=(double[])silhouette.GetEndPoint().ArrayData; return true; }
            return false;
        }
        internal static void InsertAndReplace(SldWorks app,ModelDoc2 doc,List<SwView> views,HashSet<string> generated,JobReport report,Action<string> progress)
        {
            var model=views[0].ReferencedDocument;
            var bevels=Find(model,progress); var drawing=(DrawingDoc)doc; var math=(MathUtility)app.GetMathUtility();
            progress("倒角识别完成："+bevels.Count+" 处，开始建立标注");
            var visibleByView=new Dictionary<string,HashSet<string>>();
            var silhouetteCache=new Dictionary<string,Dictionary<string,List<SilhouetteEdge>>>();
            int bevelIndex=0;
            foreach(var bevel in bevels)
            {
                progress("倒角标注："+(++bevelIndex)+"/"+bevels.Count+" · "+bevel.Owner);
                if(bevel.Cone!=null && !ChamferRules.CanProjectCone(bevel.SourceFeatureType,report.Recognition.Features.Any(f=>f.Type=="Chamfer" && f.Name==bevel.Owner))) {
                    report.SkippedChamfers.Add(new SkippedChamfer { Face=bevel.Id,Owner=bevel.Owner,SourceFeatureType=bevel.SourceFeatureType,Reason="unverified_cone_silhouette",AxialSetbackMm=bevel.AxialSetback*1000,RadialSetbackMm=bevel.RadialSetback*1000 });
                    string message="圆锥倒角仅识别、未标注：跳过非原生倒角的轮廓 API，保留原尺寸并要求补标 · "+bevel.Owner+" · "+bevel.SourceFeatureType;
                    report.Warnings.Add(message+" · "+bevel.Id); progress(message);
                    continue;
                }
                foreach(var view in views.Take(3))
                {
                    progress("倒角标注视图："+view.GetName2());
                    if(bevel.Cone!=null) { var t=(double[])view.ModelToViewTransform.ArrayData; if(Math.Abs(bevel.Axis[0]*t[2]+bevel.Axis[1]*t[5]+bevel.Axis[2]*t[8])>1e-7) continue; }
                    HashSet<string> visible=null;
                    if(bevel.Cone==null && !visibleByView.TryGetValue(view.GetName2(),out visible)) { visible=Visible(model,view,progress); visibleByView[view.GetName2()]=visible; }
                    foreach(var section in bevel.Cone==null?bevel.Sections:ConeSections(model,view,bevel,silhouetteCache,report,progress))
                    {
                        var n=(double[])((MathVector)((MathVector)math.CreateVector(section.Normal)).MultiplyTransform(view.ModelToViewTransform)).ArrayData;
                        if(Math.Abs(n[2])/Norm(n)<.999999 || (section.Silhouette==null && (!visible.Contains(ModelRecognizer.Persist(model,section.Edge)) || !visible.Contains(ModelRecognizer.Persist(model,section.Lead))))) continue;
                        string edgeId=section.Silhouette==null?ModelRecognizer.Persist(model,section.Edge):ModelRecognizer.PersistFromView(model,section.Silhouette);
                        var existing=Dimensions(view).FirstOrDefault(d=>d.Type2==10 && Entities(model,d).Contains(edgeId));
                        doc.ClearSelection2(true); drawing.ActivateView(view.GetName2());
                        var mid=section.A.Zip(section.B,(a,b)=>(a+b)/2).ToArray();
                        var anchor=(double[])((MathPoint)((MathPoint)math.CreatePoint(mid)).MultiplyTransform(view.ModelToViewTransform)).ArrayData;
                        var corner=(double[])((MathPoint)((MathPoint)math.CreatePoint(section.Corner)).MultiplyTransform(view.ModelToViewTransform)).ArrayData;
                        double x=anchor[0]+(corner[0]>=anchor[0]?1:-1)*.012,y=anchor[1]+(corner[1]>=anchor[1]?1:-1)*.009;
                        DisplayDimension dimension=existing;
                        bool selected=false;
                        if(dimension==null) {
                            progress("关联倒角轮廓："+bevelIndex+"/"+bevels.Count+" · "+view.GetName2());
                            if(section.Silhouette==null) selected=view.SelectEntity(section.Edge,false) && view.SelectEntity(section.Lead,true);
                            else { var data=((SelectionMgr)doc.SelectionManager).CreateSelectData(); data.View=view; selected=section.Silhouette.Select2(false,data) && section.SilhouetteLead.Select2(true,data); }
                            if(selected) { progress("创建原生倒角尺寸："+bevelIndex+"/"+bevels.Count+" · "+view.GetName2()); dimension=drawing.AddChamferDim(x,y,0) as DisplayDimension; }
                        }
                        if(dimension==null) continue;
                        silhouetteCache.Clear(); // A drawing rebuild can replace silhouette COM objects.
                        if(!doc.ForceRebuild3(false)) throw new InvalidOperationException("倒角尺寸重建失败。");
                        double length=0,angle=0;
                        if(!Values(dimension,out length,out angle) || ((Annotation)dimension.GetAnnotation()).IsDangling()) throw new InvalidOperationException("倒角尺寸回读失败："+bevel.Owner);
                        var lengths=new[]{Norm(Sub(section.Corner,section.A)),Norm(Sub(section.Corner,section.B))};
                        if(!lengths.Any(d=>Math.Abs(d-length)<1e-7) || Math.Abs(Math.Min(angle,Math.PI/2-angle)-Math.Atan2(lengths.Min(),lengths.Max()))>1e-6) throw new InvalidOperationException("倒角标注与实际几何不一致："+bevel.Owner+"，距离="+length+"，角度="+angle);
                        dimension.ChamferTextStyle=Math.Abs(angle-Math.PI/4)<1e-7?4:2; // native C distance / distance x angle
                        if(!doc.ForceRebuild3(false)) throw new InvalidOperationException("倒角文字样式重建失败。");
                        var annotation=(Annotation)dimension.GetAnnotation();
                        if(existing==null && !annotation.SetPosition2(x,y,0)) throw new InvalidOperationException("倒角尺寸定位失败。");
                        report.ChamferCallouts.Add(new ChamferCalloutEvidence { Face=bevel.Id,Owner=bevel.Owner,View=view.GetName2(),DimensionName=Name(dimension),DistanceMm=length*1000,AngleDegrees=angle*180/Math.PI,TextStyle=dimension.ChamferTextStyle,Text=Text(dimension),Position=(double[])annotation.GetPosition(),Entities=Entities(model,dimension) });
                        bevel.Annotated=true; break;
                    }
                    if(bevel.Annotated) break;
                }
                if(!bevel.Annotated) report.Warnings.Add("倒角未找到可用的正投影视图，保留原尺寸，需人工补标："+bevel.Owner+" "+bevel.Id);
                // Even a failed silhouette selection can replace host-owned silhouette objects.
                // Do not reuse those COM wrappers for another bevel.
                silhouetteCache.Clear();
            }
            ReplaceAuxiliary(doc,views,bevels,generated,report);
            doc.ClearSelection2(true); drawing.ActivateView("");
        }
        private static void ReplaceAuxiliary(ModelDoc2 doc,List<SwView> views,List<Bevel> bevels,HashSet<string> generated,JobReport report)
        {
            foreach(var view in views.Take(3))
            foreach(var display in Dimensions(view).ToList())
            {
                var dimension=display.GetDimension2(0); var annotation=(Annotation)display.GetAnnotation();
                if(!generated.Contains(view.GetName2()+"|"+dimension.FullName) || !display.IsReferenceDim() || (display.Type2!=2 && display.Type2!=7 && display.Type2!=8 && display.Type2!=5 && display.Type2!=6) || annotation.IsDangling() || dimension.GetToleranceType()!=0) continue;
                if(Enumerable.Range(1,4).Any(i=>!string.IsNullOrEmpty(display.GetText(i)) && !(i==1 && (display.GetText(i)=="<MOD-DIAM>" || display.GetText(i)=="<MOD-RAD>")))) continue;
                var attached=annotation.GetAttachedEntities3() as object[] ?? new object[0];
                var coneMatches=bevels.Where(b=>b.Annotated && b.Cone!=null && ConeAuxiliary(view,display,attached,b)).ToList();
                if(coneMatches.Count>0) { Remove(doc,display,view,coneMatches,report); continue; }
                var vertices=attached.OfType<Vertex>().ToList();
                // Do not touch hole centres, hole callouts or references to curved geometry.
                if(vertices.Count!=1 || attached.Length>2 || attached.Any(e=>!(e is Vertex) && (!(e is Edge) || !Line((Edge)e)))) continue;
                if(dimension.DimensionLineDirection==null) continue;
                var direction=Unit((double[])dimension.DimensionLineDirection.ArrayData); var p=Point(vertices[0]);
                string id=ModelRecognizer.PersistFromView(view.ReferencedDocument,vertices[0]);
                var matches=bevels.Where(b=>b.Annotated && b.Vertices.Contains(id) && b.Sections.Any(s=>Math.Abs(Dot(Unit(Sub(s.A,s.B)),direction))>1e-6)).ToList();
                if(matches.Count==0) continue;
                // A chamfer end may also define the complete length, width or thickness. Keep every extremum.
                double loX=0,loY=0,loZ=0,hiX=0,hiY=0,hiZ=0; var body=matches[0].Body;
                if(!body.GetExtremePoint(-direction[0],-direction[1],-direction[2],out loX,out loY,out loZ) || !body.GetExtremePoint(direction[0],direction[1],direction[2],out hiX,out hiY,out hiZ)) continue;
                double coordinate=Dot(p,direction),lo=Dot(new[]{loX,loY,loZ},direction),hi=Dot(new[]{hiX,hiY,hiZ},direction);
                if(coordinate<=lo+1e-7 || coordinate>=hi-1e-7) continue;
                matches=matches.Where(b=>b.Sections.Any(s=>Math.Abs(Dot(s.Corner,direction)-lo)<1e-7 || Math.Abs(Dot(s.Corner,direction)-hi)<1e-7)).ToList();
                if(matches.Count==0) continue; // An interior step/pocket still needs its independent location dimension.
                Remove(doc,display,view,matches,report);
            }
        }
        private static void Remove(ModelDoc2 doc,DisplayDimension display,SwView view,List<Bevel> matches,JobReport report)
        {
            var dimension=display.GetDimension2(0); var annotation=(Annotation)display.GetAnnotation();
            var removed=new RemovedChamferDimension { View=view.GetName2(),DimensionName=dimension.FullName,ValueMm=dimension.SystemValue*1000,ChamferFaces=matches.Select(b=>b.Id).ToList() };
            doc.ClearSelection2(true);
            if(!annotation.Select3(false,null) || !doc.Extension.DeleteSelection2(0)) throw new InvalidOperationException("倒角辅助尺寸删除失败："+removed.DimensionName);
            report.RemovedChamferDimensions.Add(removed);
        }
        private static bool ConeAuxiliary(SwView view,DisplayDimension display,object[] attached,Bevel bevel)
        {
            if(attached.Length<1 || attached.Length>2 || attached.Any(e=>!(e is Edge) || !((Curve)((Edge)e).GetCurve()).IsCircle())) return false;
            var ids=attached.Select(e=>ModelRecognizer.PersistFromView(view.ReferencedDocument,e)).ToList();
            var dimension=display.GetDimension2(0); double value=dimension.SystemValue;
            var direction=dimension.DimensionLineDirection==null?null:Unit((double[])dimension.DimensionLineDirection.ArrayData);
            // The end-face diameter/radius follows from the adjacent cylinder plus this complete chamfer callout.
            // Never remove the cylinder diameter or any authored model dimension.
            if(ids.Distinct().Count()==1 && ids[0]==bevel.CapCircle) {
                if(display.Type2==6) return Math.Abs(value-2*bevel.CapRadius)<1e-7;
                if(display.Type2==5) return Math.Abs(value-bevel.CapRadius)<1e-7;
                return direction!=null && Math.Abs(Dot(direction,bevel.Axis))<1e-7 && Math.Abs(value-2*bevel.CapRadius)<1e-7;
            }
            if(ids.Count!=2 || direction==null || Math.Abs(Dot(direction,bevel.Axis))<.999999) return false;
            if(ids.All(bevel.ConeCircles.Contains) && ids.Distinct().Count()==2) return Math.Abs(value-bevel.AxialSetback)<1e-7;
            // A baseline ending at the cylinder/chamfer junction is redundant only when the
            // virtual sharp corner and the baseline's other reference are both body extrema.
            var circles=attached.Cast<Edge>().Select(e=>(double[])((Curve)e.GetCurve()).CircleParams).ToList();
            double lx,ly,lz,hx,hy,hz;
            if(!bevel.Body.GetExtremePoint(-bevel.Axis[0],-bevel.Axis[1],-bevel.Axis[2],out lx,out ly,out lz) || !bevel.Body.GetExtremePoint(bevel.Axis[0],bevel.Axis[1],bevel.Axis[2],out hx,out hy,out hz)) return false;
            double lo=Dot(new[]{lx,ly,lz},bevel.Axis),hi=Dot(new[]{hx,hy,hz},bevel.Axis);
            var capEdge=((object[])bevel.Cone.GetEdges()).Cast<Edge>().First(e=>ModelRecognizer.Persist(view.ReferencedDocument,e)==bevel.CapCircle);
            double cap=Dot(((double[])((Curve)capEdge.GetCurve()).CircleParams).Take(3).ToArray(),bevel.Axis);
            if(Math.Abs(cap-lo)>1e-7 && Math.Abs(cap-hi)>1e-7) return false;
            for(int i=0;i<2;i++) if(bevel.ConeCircles.Contains(ids[i]) && ids[i]!=bevel.CapCircle) {
                double target=Dot(circles[i].Take(3).ToArray(),bevel.Axis),other=Dot(circles[1-i].Take(3).ToArray(),bevel.Axis);
                if(target>lo+1e-7 && target<hi-1e-7 && (Math.Abs(other-lo)<1e-7 || Math.Abs(other-hi)<1e-7)) return true;
            }
            return false;
        }
        internal static void Verify(DrawingDoc drawing,JobReport report,bool reopened)
        {
            foreach(var evidence in report.ChamferCallouts)
            {
                var view=Views(drawing).FirstOrDefault(v=>v.GetName2()==evidence.View);
                var d=view==null?null:Dimensions(view).FirstOrDefault(item=>Name(item)==evidence.DimensionName && item.Type2==10);
                double length=0,angle=0;
                if(d==null || !Values(d,out length,out angle) || Math.Abs(length*1000-evidence.DistanceMm)>1e-5 || Math.Abs(angle*180/Math.PI-evidence.AngleDegrees)>1e-5 || d.ChamferTextStyle!=evidence.TextStyle || ((Annotation)d.GetAnnotation()).IsDangling() || evidence.Entities.Count<2 || !evidence.Entities.All(e=>Entities(view.ReferencedDocument,d).Contains(e))) throw new InvalidOperationException("倒角标注关联或保存重开校验失败："+evidence.DimensionName+"，找到="+(d!=null)+"，距离="+length*1000+"，角度="+angle*180/Math.PI+"，样式="+(d==null?-1:d.ChamferTextStyle));
                evidence.Position=(double[])((Annotation)d.GetAnnotation()).GetPosition(); evidence.Text=Text(d); evidence.Reopened=reopened;
            }
        }
    }
}
