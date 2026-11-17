using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;
using SwView=SolidWorks.Interop.sldworks.View;

namespace AutoDrawing
{
    public sealed class RadiusCalloutEvidence
    {
        public string Geometry { get; set; }
        public string Owner { get; set; }
        public string View { get; set; }
        public string DimensionName { get; set; }
        public double RadiusMm { get; set; }
        public string Text { get; set; }
        public List<string> Entities { get; set; }
        public double[] Position { get; set; }
        public bool Reopened { get; set; }
        public List<string> ReplacedAuxiliaryDimensions { get; set; }
        public bool CreatedByGenerator { get; set; }
        public int Quantity { get; set; }
        public List<string> MemberGeometries { get; set; }
    }
    internal static class FilletAnnotations
    {
        private static IEnumerable<DisplayDimension> Dimensions(SwView view)
        { for(var d=view.GetFirstDisplayDimension5();d!=null;d=d.GetNext5()) yield return d; }
        private static List<string> Entities(SwView view,DisplayDimension d)
        { return (((Annotation)d.GetAnnotation()).GetAttachedEntities3() as object[] ?? new object[0]).Select(e=>ModelRecognizer.PersistFromView(view.ReferencedDocument,e)).Where(x=>x!="").Distinct().ToList(); }
        private static bool TangentRound(ModelDoc2 model,Face2 face,RecognizedGeometry g)
        {
            var supports=new HashSet<string>();
            foreach(Edge edge in face.GetEdges() as object[] ?? new object[0]) {
                var curve=edge.GetCurve() as Curve;
                if(curve==null || !curve.IsLine() || edge.GetStartVertex()==null) continue;
                var p=(double[])((Vertex)edge.GetStartVertex()).GetPoint(); var delta=p.Zip(g.Center,(x,y)=>x-y).ToArray(); double axial=Dot(delta,g.Axis);
                var radial=delta.Zip(g.Axis,(x,y)=>x-axial*y).ToArray();
                foreach(Face2 neighbor in edge.GetTwoAdjacentFaces2() as object[] ?? new object[0]) {
                    var surface=neighbor.GetSurface() as Surface;
                    if(surface==null || !surface.IsPlane()) continue;
                    var n=(double[])neighbor.Normal;
                    if(Math.Abs(Dot(n,g.Axis))<1e-7 && Math.Abs(Math.Abs(Dot(n,radial))-g.DiameterMm/2000)<1e-7) supports.Add(ModelRecognizer.Persist(model,neighbor));
                }
            }
            return supports.Count>=2;
        }
        internal static void Insert(ModelDoc2 doc,List<SwView> views,HashSet<string> generated,JobReport report,Action<string> progress)
        {
            var model=views[0].ReferencedDocument; if(model.GetType()!=1) return;
            var slotEntities=new HashSet<string>(report.Recognition.Geometry.Where(g=>g.Kind=="slot").SelectMany(g=>g.Entities));
            var variableOwners=new HashSet<string>(report.Recognition.Features.Where(f=>f.Type=="VarFillet").Select(f=>f.Name));
            var candidates=report.Recognition.Geometry.Where(g=>(g.Kind=="external_arc" || g.Kind=="internal_arc") && g.AngularSpan<Math.PI+1e-6 && !slotEntities.Contains(g.Id) && !variableOwners.Contains(g.Owner)).ToList();
            if(candidates.Count==0) return;
            var faces=((PartDoc)model).GetBodies2(0,false) as object[] ?? new object[0];
            var faceMap=faces.Cast<Body2>().SelectMany(b=>(b.GetFaces() as object[] ?? new object[0]).Cast<Face2>()).ToDictionary(f=>ModelRecognizer.Persist(model,f));
            candidates=candidates.Where(g=>faceMap.ContainsKey(g.Id) && TangentRound(model,faceMap[g.Id],g)).ToList();
            var visibleByView=new Dictionary<string,Dictionary<string,Edge>>(); int index=0;
            foreach(var g in candidates) {
                progress("圆角标注："+(++index)+"/"+candidates.Count+" · "+g.Owner);
                bool inserted=false;
                foreach(var view in views.Take(3)) {
                    var t=(double[])view.ModelToViewTransform.ArrayData;
                    double z=g.Axis[0]*t[2]+g.Axis[1]*t[5]+g.Axis[2]*t[8];
                    if(Math.Abs(z)<.999999) continue;
                    Dictionary<string,Edge> visible;
                    if(!visibleByView.TryGetValue(view.GetName2(),out visible)) {
                    progress("圆角可见圆弧读取："+view.GetName2());
                    visible=new Dictionary<string,Edge>();
                    foreach(Component2 component in view.GetVisibleComponents() as object[] ?? new object[0])
                    foreach(Edge edge in view.GetVisibleEntities2(component,1) as object[] ?? new object[0]) {
                        var visibleCurve=edge.GetCurve() as Curve; if(visibleCurve==null || !visibleCurve.IsCircle()) continue;
                        string id=ModelRecognizer.PersistFromView(model,edge); if(id!="") visible[id]=edge;
                    }
                    visibleByView[view.GetName2()]=visible;
                    }
                    var edgeId=g.Entities.FirstOrDefault(visible.ContainsKey); if(edgeId==null) continue;
                    var arcEdge=visible[edgeId]; var curve=(Curve)arcEdge.GetCurve(); double a=0,b=0;
                    var arcParameters=(CurveParamData)arcEdge.GetCurveParams3(); a=arcParameters.UMinValue; b=arcParameters.UMaxValue;
                    if(Math.Abs(b-a)>=2*Math.PI-1e-6 || Math.Abs(((double[])curve.CircleParams)[6]*2000-g.DiameterMm)>1e-5) continue;
                    var dimension=Dimensions(view).FirstOrDefault(d=>d.Type2==5 && Entities(view,d).Intersect(g.Entities).Any());
                    bool created=dimension==null || generated.Contains(view.GetName2()+"|"+dimension.GetDimension2(0).FullName);
                    doc.ClearSelection2(true); ((DrawingDoc)doc).ActivateView(view.GetName2());
                    if(dimension==null) {
                        var box=(double[])view.GetOutline();
                        if(!view.SelectEntity(arcEdge,false)) continue;
                        dimension=doc.AddRadialDimension2(box[2]+.012,box[3]+.008,0) as DisplayDimension;
                    }
                    if(dimension==null) continue;
                    var ann=(Annotation)dimension.GetAnnotation();
                    if(dimension.Type2!=5 || Math.Abs(dimension.GetDimension2(0).SystemValue*2000-g.DiameterMm)>1e-5 || ann.IsDangling()) throw new InvalidOperationException("圆角 R 标注回读失败："+g.Owner);
                    report.RadiusCallouts.Add(new RadiusCalloutEvidence { Geometry=g.Id,Owner=g.Owner,View=view.GetName2(),DimensionName=dimension.GetDimension2(0).FullName.Split('@')[0],RadiusMm=g.DiameterMm/2,Text=ChamferAnnotations.Text(dimension),Entities=Entities(view,dimension),Position=(double[])ann.GetPosition(),ReplacedAuxiliaryDimensions=new List<string>(),CreatedByGenerator=created,Quantity=1,MemberGeometries=new List<string> { g.Id } });
                    inserted=true; break;
                }
                if(!inserted) report.Warnings.Add("圆弧/圆角未找到可正视的可见圆弧，保留原视图并要求补标 R："+g.Owner);
            }
            foreach(var f in report.Recognition.Features.Where(f=>f.Category=="fillet" && !report.RadiusCallouts.Any(r=>r.Owner==f.Name))) report.Warnings.Add("圆角特征尚未建立恒定 R 标注："+f.Name+"；变半径、样条过渡及非正视圆弧需局部/剖视复核。");
            ReplaceAuxiliary(doc,views,generated,report);
            if(report.Options.Deduplicate) GroupRepeatedRadii(doc,views,report);
            doc.ClearSelection2(true); ((DrawingDoc)doc).ActivateView("");
        }
        private static void GroupRepeatedRadii(ModelDoc2 doc,List<SwView> views,JobReport report)
        {
            var groups=report.RadiusCallouts.Where(r=>r.CreatedByGenerator).GroupBy(r=> {
                var g=report.Recognition.Geometry.First(x=>x.Id==r.Geometry);
                return r.View+"|"+g.Body+"|"+g.Owner+"|"+string.Join(",",g.Axis.Select(x=>Math.Round(x,7)))+"|"+Math.Round(g.DiameterMm,6)+"|"+Math.Round(g.AngularSpan,7)+"|"+Math.Round(g.DepthMm,6);
            }).Where(g=>g.Count()>1).Select(g=>g.ToList()).ToList();
            foreach(var group in groups) {
                var first=group[0]; var view=views.First(v=>v.GetName2()==first.View);
                var dimensions=Dimensions(view).ToDictionary(d=>d.GetDimension2(0).FullName.Split('@')[0]);
                if(group.Any(r=>!dimensions.ContainsKey(r.DimensionName) || dimensions[r.DimensionName].GetDimension2(0).GetToleranceType()!=0 || Enumerable.Range(2,3).Any(i=>!string.IsNullOrEmpty(dimensions[r.DimensionName].GetText(i))))) continue;
                var prefixes=group.ToDictionary(r=>r.DimensionName,r=>Regex.Match((dimensions[r.DimensionName].GetText(1) ?? "").Trim(),@"^(?:(\d+)\s*[Xx×]\s*)?(R|<MOD-RAD>)?$"));
                if(prefixes.Values.Any(m=>!m.Success || (m.Groups[1].Success && m.Groups[1].Value!=group.Count.ToString()))) continue;
                // AutoDimension may already supply n X R on one corner. Keep that native callout.
                first=group.OrderByDescending(r=>prefixes[r.DimensionName].Groups[1].Success).First();
                var ordered=new[]{first}.Concat(group.Where(r=>r!=first)).ToList();
                var keeper=dimensions[first.DimensionName]; string symbol=prefixes[first.DimensionName].Groups[2].Value;
                foreach(var extra in ordered.Skip(1)) {
                    var ann=(Annotation)dimensions[extra.DimensionName].GetAnnotation(); doc.ClearSelection2(true);
                    if(!ann.Select3(false,null) || !doc.Extension.DeleteSelection2(0)) { report.Warnings.Add("重复圆角标注合并失败："+extra.DimensionName); continue; }
                    first.MemberGeometries.Add(extra.Geometry); first.ReplacedAuxiliaryDimensions.AddRange(extra.ReplacedAuxiliaryDimensions);
                    report.RadiusCallouts.Remove(extra); report.RemovedDuplicates++;
                    report.RemovedRedundantDimensions.Add(new RemovedRedundantDimension { View=extra.View,Dimension=extra.DimensionName,ValueSI=extra.RadiusMm/1000,Reason="radius_quantity",Start=extra.Geometry,End=first.Geometry,RetainedDimensions=new List<string> { first.View+"|"+first.DimensionName } });
                }
                first.Quantity=first.MemberGeometries.Count;
                if(first.Quantity>1) { keeper.SetText(1,first.Quantity+"X"+symbol); first.Text=ChamferAnnotations.Text(keeper); }
            }
        }
        private static double Dot(double[] a,double[] b) { return a.Zip(b,(x,y)=>x*y).Sum(); }
        private static void ReplaceAuxiliary(ModelDoc2 doc,List<SwView> views,HashSet<string> generated,JobReport report)
        {
            var model=views[0].ReferencedDocument;
            foreach(var evidence in report.RadiusCallouts) {
                var g=report.Recognition.Geometry.First(x=>x.Id==evidence.Geometry);
                if(Math.Abs(g.AngularSpan-Math.PI/2)>1e-6) continue;
                var body=(Body2)((object[])((PartDoc)model).GetBodies2(0,false))[g.Body];
                var face=((object[])body.GetFaces()).Cast<Face2>().First(f=>ModelRecognizer.Persist(model,f)==g.Id);
                var vertices=new HashSet<string>(); var corners=new List<double[]>(); var arcIds=new HashSet<string>();
                foreach(Edge edge in (object[])face.GetEdges()) {
                    if(!((Curve)edge.GetCurve()).IsCircle() || edge.GetStartVertex()==null || edge.GetEndVertex()==null) continue;
                    var va=(Vertex)edge.GetStartVertex(); var vb=(Vertex)edge.GetEndVertex(); var a=(double[])va.GetPoint(); var b=(double[])vb.GetPoint();
                    double axial=Dot(a.Zip(g.Center,(x,y)=>x-y).ToArray(),g.Axis);
                    var center=g.Center.Zip(g.Axis,(x,y)=>x+axial*y).ToArray(); corners.Add(a.Zip(b,(x,y)=>x+y).Zip(center,(x,y)=>x-y).ToArray());
                    vertices.Add(ModelRecognizer.Persist(model,va)); vertices.Add(ModelRecognizer.Persist(model,vb));
                    arcIds.Add(ModelRecognizer.Persist(model,edge));
                }
                foreach(var view in views.Take(3)) foreach(var d in Dimensions(view).ToList()) {
                    var dimension=d.GetDimension2(0); var ann=(Annotation)d.GetAnnotation();
                    if(d.Type2!=2 || !generated.Contains(view.GetName2()+"|"+dimension.FullName) || !d.IsReferenceDim() || dimension.GetToleranceType()!=0 || Enumerable.Range(1,4).Any(i=>!string.IsNullOrEmpty(d.GetText(i)))) continue;
                    var attached=ann.GetAttachedEntities3() as object[] ?? new object[0]; var target=attached.OfType<Vertex>().ToList();
                    var ids=attached.Select(x=>ModelRecognizer.PersistFromView(model,x)).ToList();
                    bool vertexTarget=target.Count==1 && vertices.Contains(ModelRecognizer.PersistFromView(model,target[0]));
                    bool arcTarget=ids.Any(arcIds.Contains),projection=ids.Count>0 && ids.All(g.Entities.Contains) && Math.Abs(dimension.SystemValue-evidence.RadiusMm/1000)<1e-7;
                    if((!vertexTarget && !arcTarget && !projection) || dimension.DimensionLineDirection==null) continue;
                    var direction=(double[])dimension.DimensionLineDirection.ArrayData; if(Math.Abs(Dot(direction,g.Axis))>1e-6) continue;
                    double lx,ly,lz,hx,hy,hz; if(!body.GetExtremePoint(-direction[0],-direction[1],-direction[2],out lx,out ly,out lz) || !body.GetExtremePoint(direction[0],direction[1],direction[2],out hx,out hy,out hz)) continue;
                    double lo=Dot(new[]{lx,ly,lz},direction),hi=Dot(new[]{hx,hy,hz},direction),value=Dot(vertexTarget?(double[])target[0].GetPoint():g.Center,direction);
                    if(value<=lo+1e-7 || value>=hi-1e-7 || !corners.Any(c=>Math.Abs(Dot(c,direction)-lo)<1e-7 || Math.Abs(Dot(c,direction)-hi)<1e-7)) continue;
                    if(Math.Abs(dimension.SystemValue-Math.Abs(value-lo))>1e-7 && Math.Abs(dimension.SystemValue-Math.Abs(hi-value))>1e-7) continue;
                    string name=dimension.FullName; doc.ClearSelection2(true);
                    if(!ann.Select3(false,null) || !doc.Extension.DeleteSelection2(0)) throw new InvalidOperationException("圆角辅助尺寸替换失败："+name);
                    evidence.ReplacedAuxiliaryDimensions.Add(name);
                }
            }
        }
        internal static void Verify(DrawingDoc drawing,JobReport report,bool reopened)
        {
            foreach(var evidence in report.RadiusCallouts) {
                var view=((SwView)drawing.GetFirstView()).GetNextView() as SwView;
                while(view!=null && view.GetName2()!=evidence.View) view=view.GetNextView() as SwView;
                var d=view==null?null:Dimensions(view).FirstOrDefault(x=>x.GetDimension2(0).FullName.Split('@')[0]==evidence.DimensionName && x.Type2==5);
                if(d==null || Math.Abs(d.GetDimension2(0).SystemValue*1000-evidence.RadiusMm)>1e-5 || ((Annotation)d.GetAnnotation()).IsDangling() || evidence.Entities.Count==0 || !evidence.Entities.All(Entities(view,d).Contains)) throw new InvalidOperationException("圆角 R 尺寸关联/重开校验失败："+evidence.Owner);
                if(evidence.Quantity>1 && (evidence.MemberGeometries.Count!=evidence.Quantity || !(d.GetText(1) ?? "").StartsWith(evidence.Quantity+"X",StringComparison.Ordinal))) throw new InvalidOperationException("圆角数量标注重开校验失败："+evidence.Owner);
                evidence.Text=ChamferAnnotations.Text(d); evidence.Position=(double[])((Annotation)d.GetAnnotation()).GetPosition(); evidence.Reopened=reopened;
            }
        }
    }
}
