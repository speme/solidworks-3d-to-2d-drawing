using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using SolidWorks.Interop.sldworks;

namespace AutoDrawing
{
    public sealed class RecognizedGeometry
    {
        public string Id { get; set; }
        public string Kind { get; set; }
        public string Owner { get; set; }
        public string SourceFeatureType { get; set; }
        public int Body { get; set; }
        public double[] Axis { get; set; }
        public double[] Center { get; set; }
        public double DiameterMm { get; set; }
        public double MinimumDiameterMm { get; set; }
        public double DepthMm { get; set; }
        public double LengthMm { get; set; }
        public double? AxialMinMm { get; set; }
        public double? AxialMaxMm { get; set; }
        public double AngularSpan { get; set; }
        public string Extent { get; set; }
        public string SizeCoverage { get; set; }
        public List<string> Entities { get; set; }
        public List<string> AdjacentPlanes { get; set; }
        public string OpenEndView { get; set; }
    }
    public sealed class FeatureEvidence
    { public string Name { get; set; } public string Type { get; set; } public string Category { get; set; } }
    public sealed class HoleGroup
    { public string Kind { get; set; } public double DiameterMm { get; set; } public int Count { get; set; } public List<string> Members { get; set; } }
    public sealed class RecognitionReport
    {
        public string PartClass { get; set; }
        public string PrimaryView { get; set; }
        public int BodyCount { get; set; }
        public RackGeometryEvidence RackGeometry { get; set; }
        public double[] BoundsMm { get; set; }
        public List<RecognizedGeometry> Geometry { get; set; }
        public List<FeatureEvidence> Features { get; set; }
        public List<HoleGroup> HoleGroups { get; set; }
        public List<string> Notes { get; set; }
        public List<ThreadSource> Threads { get; set; }
        public RecognitionReport() { Geometry=new List<RecognizedGeometry>(); Features=new List<FeatureEvidence>(); HoleGroups=new List<HoleGroup>(); Notes=new List<string>(); Threads=new List<ThreadSource>(); PrimaryView="Front"; }
    }
    public static class RecognitionRules
    {
        public static double[] Axis(double[] a)
        {
            double length=Math.Sqrt(a.Sum(x=>x*x));
            if(length<1e-12) throw new ArgumentException("无效轴向");
            var n=a.Select(x=>x/length).ToArray();
            int first=Array.FindIndex(n,x=>Math.Abs(x)>1e-8);
            return first>=0 && n[first]<0?n.Select(x=>-x).ToArray():n;
        }
        public static string AxisLine(RecognizedGeometry g)
        {
            var n=Axis(g.Axis); double dot=n.Select((x,i)=>x*g.Center[i]).Sum();
            return g.Body+"|"+string.Join(",",n.Concat(g.Center.Select((x,i)=>x-n[i]*dot)).Select(x=>Math.Round(x,7).ToString("F7",CultureInfo.InvariantCulture)));
        }
        public static string Category(string type)
        {
            switch(type)
            {
                case "HoleWzd": return "hole_wizard";
                case "Chamfer": return "chamfer";
                case "Fillet": case "VarFillet": return "fillet";
                case "LPattern": case "CirPattern": case "MirrorPattern": return "pattern";
                case "SheetMetal": case "SMBaseFlange": case "FlatPattern": return "sheet_metal";
                case "CosmeticThread": case "Thread": return "thread_feature";
                case "Cut": case "CutThin": case "ExtrusionCut": return "pocket_or_cut";
                case "Revolution": return "revolve";
                case "Imported": case "MBimport": return "imported";
                default: return "other";
            }
        }
        public static string ChooseView(RecognitionReport report)
        {
            if(report.BoundsMm==null || report.BoundsMm.Length!=6) return "Front";
            double[] size=Enumerable.Range(0,3).Select(i=>report.BoundsMm[i+3]-report.BoundsMm[i]).ToArray();
            // End-on holes carry more detail than a large blank face. Tie preference is Front.
            int[] normalAxes={2,1,0,2,1,0}; string[] names={"Front","Top","Right","Back","Bottom","Left"};
            double best=double.NegativeInfinity; string winner="Front";
            for(int i=0;i<names.Length;i++)
            {
                int axis=normalAxes[i]; double area=size[(axis+1)%3]*size[(axis+2)%3];
                double detail=report.Geometry.Where(g=>g.Kind=="hole" || g.Kind=="slot").Sum(g=>Math.Abs(g.Axis[axis])>.999?1.0:0.0);
                double opening=report.Geometry.Count(g=>g.Kind=="hole" && g.OpenEndView==names[i]);
                double score=detail*10+opening*5+area/Math.Max(1,size.Max()*size.Max());
                if(score>best+1e-8) { best=score; winner=names[i]; }
            }
            return winner;
        }
        public static void Finish(RecognitionReport report)
        {
            var holes=report.Geometry.Where(g=>g.Kind=="hole").ToList();
            foreach(var group in holes.GroupBy(g=>g.Extent+"|"+Math.Round(g.DiameterMm,5)+"|"+string.Join(",",g.Axis.Select(x=>Math.Round(x,5).ToString(CultureInfo.InvariantCulture)))+"|"+Math.Round(g.DepthMm,4)))
                report.HoleGroups.Add(new HoleGroup { Kind=group.First().Extent,DiameterMm=group.First().DiameterMm,Count=group.Count(),Members=group.Select(g=>g.Id).ToList() });
            foreach(var bore in holes.GroupBy(AxisLine).Where(g=>g.Select(x=>Math.Round(x.DiameterMm,4)).Distinct().Count()>1))
                report.Notes.Add("同轴阶梯孔候选："+string.Join(",",bore.Select(g=>g.Id))+"；沉孔/沉头语义需结合特征和截面复核。");
            report.PrimaryView=ChooseView(report);
        }
    }
    public static class ModelRecognizer
    {
        // Drawing entities are component-context proxies. Asking a part for their persistent ID
        // directly can return one identical placeholder for unrelated edges. Convert them first.
        internal static string PersistFromView(ModelDoc2 model,object entity)
        {
            if(model==null || entity==null) return "";
            var silhouette=entity as SilhouetteEdge;
            if(silhouette!=null) { string face=PersistFromView(model,silhouette.GetFace()); return face==""?"":"silhouette:"+face; }
            if(model.GetType()==1 && (entity is Edge || entity is Vertex || entity is Face2)) {
                var native=((PartDoc)model).GetCorrespondingEntity(entity);
                if(native==null) return "";
                return Persist(model,native);
            }
            return Persist(model,entity);
        }
        public static string Persist(ModelDoc2 model,object entity)
        {
            if(entity==null) return "";
            try { var bytes=model.Extension.GetPersistReference3(entity) as byte[]; if(bytes==null || bytes.Length==0) return ""; using(var hash=SHA256.Create()) return "sha256:"+Convert.ToBase64String(hash.ComputeHash(bytes)); }
            catch(System.Runtime.InteropServices.COMException) { return ""; }
        }
        public static RecognitionReport Read(ModelDoc2 model) { return Read(model,null); }
        public static RecognitionReport Read(ModelDoc2 model,Action<string> progress)
        {
            var result=new RecognitionReport();
            if(model.GetType()!=1) { result.PartClass="assembly"; result.Notes.Add("装配体按整体出图，特征识别不跨实例推断；需要逐件出图时加入组件队列。"); return result; }
            foreach(var node in FeatureTraversal.Active(model))
            {
                var feature=node.Feature;
                string type=feature.GetTypeName2();
                if(RecognitionRules.Category(type)!="other") result.Features.Add(new FeatureEvidence { Name=feature.Name,Type=type,Category=RecognitionRules.Category(type) });
                ThreadAnnotations.ReadSource(model,node,result);
            }
            var bodies=((PartDoc)model).GetBodies2(0,false) as object[] ?? new object[0]; result.BodyCount=bodies.Length;
            if(bodies.Length==0) throw new InvalidOperationException("模型没有可出图实体。");
            double[] bounds={double.PositiveInfinity,double.PositiveInfinity,double.PositiveInfinity,double.NegativeInfinity,double.NegativeInfinity,double.NegativeInfinity};
            var rackPlanes=new List<RackPlaneSample>();
            for(int bi=0;bi<bodies.Length;bi++)
            {
                var body=(Body2)bodies[bi]; var box=(double[])body.GetBodyBox();
                var spans=Enumerable.Range(0,3).Select(i=>box[i+3]-box[i]).OrderByDescending(x=>x).ToArray();
                bool longBody=bodies.Length==1 && spans[0]>3.5*spans[1];
                for(int k=0;k<3;k++) { bounds[k]=Math.Min(bounds[k],box[k]); bounds[k+3]=Math.Max(bounds[k+3],box[k+3]); }
                var faces=body.GetFaces() as object[] ?? new object[0];
                int faceIndex=0;
                foreach(Face2 face in faces)
                {
                    if(progress!=null && faceIndex++%25==0) progress("几何识别：实体 "+(bi+1)+"/"+bodies.Length+"，面 "+faceIndex+"/"+faces.Length);
                    try
                    {
                    var surface=(Surface)face.GetSurface();
                    if(longBody && surface.IsPlane()) { var pp=(double[])surface.PlaneParams; rackPlanes.Add(new RackPlaneSample { Normal=pp.Take(3).ToArray(),Point=pp.Skip(3).Take(3).ToArray(),AreaMm2=face.GetArea()*1000000 }); }
                    if(!surface.IsCylinder() && !surface.IsCone()) continue;
                    Feature owner=(Feature)face.GetFeature();
                    if(surface.IsCone()) {
                        var cone=new RecognizedGeometry { Id=Persist(model,face),Kind="cone",Body=bi,Owner=owner==null?"":owner.Name,SourceFeatureType=owner==null?"":owner.GetTypeName2(),Entities=new List<string>(),AdjacentPlanes=new List<string>(),SizeCoverage="review_required" };
                        if(longBody) {
                            var circles=(face.GetEdges() as object[] ?? new object[0]).Cast<Edge>().Select(e=>e.GetCurve() as Curve).Where(c=>c!=null && c.IsCircle()).Select(c=>(double[])c.CircleParams).ToList();
                            if(circles.Count>=2) {
                                cone.Axis=RecognitionRules.Axis(circles[0].Skip(3).Take(3).ToArray()); cone.Center=circles[0].Take(3).ToArray();
                                var levels=circles.Select(c=>Enumerable.Range(0,3).Sum(k=>c[k]*cone.Axis[k])*1000).ToList();
                                cone.AxialMinMm=levels.Min(); cone.AxialMaxMm=levels.Max(); cone.DepthMm=levels.Max()-levels.Min();
                                cone.DiameterMm=circles.Max(c=>c[6])*2000; cone.MinimumDiameterMm=circles.Min(c=>c[6])*2000;
                            }
                        }
                        result.Geometry.Add(cone); continue;
                    }
                    var cp=(double[])surface.CylinderParams;
                    var axis=RecognitionRules.Axis(new[]{cp[3],cp[4],cp[5]}); var center=cp.Take(3).ToArray();
                    var uv=(double[])face.GetUVBounds();
                    double u=(uv[0]+uv[1])/2,v=(uv[2]+uv[3])/2;
                    var point=(double[])surface.Evaluate(u,v,0,0);
                    var normal=(double[])surface.EvaluateAtPoint(point[0],point[1],point[2]);
                    double projection=Enumerable.Range(0,3).Sum(i=>(point[i]-center[i])*axis[i]);
                    double dot=Enumerable.Range(0,3).Sum(i=>(point[i]-center[i]-projection*axis[i])*normal[i])*(face.FaceInSurfaceSense()?-1:1);
                    bool inside=dot<0,full=Math.Abs(uv[1]-uv[0]-2*Math.PI)<1e-5;
                    var entities=new List<string>{Persist(model,face)}; var adjacent=new HashSet<string>(); var axial=new List<double>(); bool floor=false;
                    foreach(Edge edge in (face.GetEdges() as object[] ?? new object[0]))
                    {
                        entities.Add(Persist(model,edge)); var curve=(Curve)edge.GetCurve();
                        if(curve.IsCircle()) { var p=(double[])curve.CircleParams; axial.Add(Enumerable.Range(0,3).Sum(i=>p[i]*axis[i])); }
                        foreach(Face2 neighbour in (edge.GetTwoAdjacentFaces2() as object[] ?? new object[0]))
                        {
                            if(neighbour==null) continue;
                            var ns=(Surface)neighbour.GetSurface();
                            if(!ns.IsPlane()) continue;
                            var planeNormal=(double[])neighbour.Normal;
                            if(planeNormal!=null && Math.Abs(Enumerable.Range(0,3).Sum(i=>planeNormal[i]*axis[i]))<1e-6) adjacent.Add(Persist(model,neighbour));
                            if(neighbour.GetLoopCount()==1 && Math.Abs(neighbour.GetArea()-Math.PI*cp[6]*cp[6])<1e-9) floor=true;
                        }
                    }
                    string extent="unknown"; double depth=axial.Count>=2?axial.Max()-axial.Min():0;
                    int principal=Array.FindIndex(axis,x=>Math.Abs(x)>.999999);
                    if(full && inside && principal>=0 && axial.Count>=2)
                        extent=Math.Abs(axial.Min()-box[principal])<1e-7 && Math.Abs(axial.Max()-box[principal+3])<1e-7?"through":floor?"blind":"partial";
                    var g=new RecognizedGeometry { Id=Persist(model,face),Kind=inside?(full?"hole":"internal_arc"):full?"outer_cylinder":"external_arc",Body=bi,Owner=owner==null?"":owner.Name,Axis=axis,Center=center,DiameterMm=cp[6]*2000,DepthMm=depth*1000,AxialMinMm=axial.Count>=2?(double?)(axial.Min()*1000):null,AxialMaxMm=axial.Count>=2?(double?)(axial.Max()*1000):null,AngularSpan=uv[1]-uv[0],Extent=extent,Entities=entities.Where(x=>x!="").Distinct().ToList(),AdjacentPlanes=adjacent.Where(x=>x!="").ToList(),SizeCoverage="missing" };
                    if(full && inside && principal>=0 && axial.Count>=2)
                    {
                        bool low=Math.Abs(axial.Min()-box[principal])<1e-7,high=Math.Abs(axial.Max()-box[principal+3])<1e-7;
                        if(low!=high) g.OpenEndView=(low?new[]{"Left","Bottom","Back"}:new[]{"Right","Top","Front"})[principal];
                    }
                    result.Geometry.Add(g);
                    }
                    catch(System.Runtime.InteropServices.COMException ex) { result.Notes.Add("局部曲面无法读取，保留原生视图并要求复核："+ex.ErrorCode); }
                }
            }
            // A slot requires two semicylindrical ends sharing BOTH planar connector walls.
            var arcs=result.Geometry.Where(g=>g.Kind=="internal_arc" && Math.Abs(g.AngularSpan-Math.PI)<1e-5).ToArray(); var paired=new HashSet<string>();
            for(int i=0;i<arcs.Length;i++) for(int j=i+1;j<arcs.Length;j++)
            {
                var a=arcs[i]; var b=arcs[j];
                if(paired.Contains(a.Id) || paired.Contains(b.Id) || a.Body!=b.Body || Math.Abs(a.DiameterMm-b.DiameterMm)>1e-5 || a.Axis.Where((x,k)=>Math.Abs(x-b.Axis[k])>1e-6).Any() || a.AdjacentPlanes.Intersect(b.AdjacentPlanes).Count()<2) continue;
                var delta=a.Center.Select((x,k)=>x-b.Center[k]).ToArray(); double axial=delta.Select((x,k)=>x*a.Axis[k]).Sum();
                double distance=Math.Sqrt(delta.Sum(x=>x*x)-axial*axial);
                if(distance<1e-7) continue;
                result.Geometry.Add(new RecognizedGeometry { Id="slot:"+a.Id+":"+b.Id,Kind="slot",Body=a.Body,Owner=a.Owner,Axis=a.Axis,Center=a.Center.Select((x,k)=>(x+b.Center[k])/2).ToArray(),DiameterMm=a.DiameterMm,LengthMm=distance*1000+a.DiameterMm,Entities=a.Entities.Concat(b.Entities).Distinct().ToList(),AdjacentPlanes=new List<string>(),SizeCoverage="missing" }); paired.Add(a.Id); paired.Add(b.Id);
            }
            result.BoundsMm=bounds.Select(x=>x*1000).ToArray();
            var sizes=Enumerable.Range(0,3).Select(i=>bounds[i+3]-bounds[i]).OrderBy(x=>x).ToArray();
            result.PartClass=bodies.Length>1?"multi_body":result.Features.Any(f=>f.Category=="sheet_metal")?"sheet_metal":result.Geometry.Any(g=>g.Kind=="outer_cylinder" && g.DepthMm>g.DiameterMm*2)?"shaft":sizes[0]<sizes[2]*.2?"plate":result.Geometry.Any(g=>g.Kind=="outer_cylinder")?"turned_or_cylindrical":"prismatic";
            result.RackGeometry=result.Features.Any(f=>f.Category=="sheet_metal")?null:RackRules.Detect(rackPlanes,result.BoundsMm);
            RecognitionRules.Finish(result); return result;
        }
        public static void Coverage(RecognitionReport recognition,IEnumerable<DimensionEvidence> dimensions)
        {
            foreach(var geometry in recognition.Geometry.Where(g=>g.Entities!=null))
            {
                bool linked=dimensions.Any(d=>!d.Dangling && (d.Type==5 || d.Type==6) && Math.Abs(d.ValueSI*1000*(d.Type==5?2:1)-geometry.DiameterMm)<.005 && d.Entities!=null && d.Entities.Intersect(geometry.Entities).Any());
                geometry.SizeCoverage=linked?"linked_size_dimension":"review_required";
            }
        }
    }
}
