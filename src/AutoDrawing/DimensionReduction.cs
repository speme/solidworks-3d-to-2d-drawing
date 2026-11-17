using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SwView=SolidWorks.Interop.sldworks.View;

namespace AutoDrawing
{
    public sealed class RemovedRedundantDimension
    {
        public string View { get; set; }
        public string Dimension { get; set; }
        public double ValueSI { get; set; }
        public string Reason { get; set; }
        public List<string> RetainedDimensions { get; set; }
        public string Start { get; set; }
        public string End { get; set; }
    }
    // Nodes are identified model entities, never rounded numeric dimension values.
    public sealed class DimensionConstraintGraph
    {
        private readonly Dictionary<string,List<Tuple<string,string>>> edges=new Dictionary<string,List<Tuple<string,string>>>();
        public List<string> Path(string start,string end)
        {
            if(string.IsNullOrEmpty(start) || string.IsNullOrEmpty(end) || start==end) return null;
            var queue=new Queue<string>(); var visited=new HashSet<string>();
            var previous=new Dictionary<string,Tuple<string,string>>(); queue.Enqueue(start); visited.Add(start);
            while(queue.Count>0) {
                string at=queue.Dequeue(); List<Tuple<string,string>> next;
                if(!edges.TryGetValue(at,out next)) continue;
                foreach(var edge in next) if(visited.Add(edge.Item1)) {
                    previous[edge.Item1]=Tuple.Create(at,edge.Item2);
                    if(edge.Item1==end) {
                        var path=new List<string>(); string node=end;
                        while(node!=start) { var p=previous[node]; path.Add(p.Item2); node=p.Item1; }
                        path.Reverse(); return path;
                    }
                    queue.Enqueue(edge.Item1);
                }
            }
            return null;
        }
        public void Add(string start,string end,string dimension)
        {
            if(string.IsNullOrEmpty(start) || string.IsNullOrEmpty(end) || start==end) return;
            if(!edges.ContainsKey(start)) edges[start]=new List<Tuple<string,string>>();
            if(!edges.ContainsKey(end)) edges[end]=new List<Tuple<string,string>>();
            edges[start].Add(Tuple.Create(end,dimension)); edges[end].Add(Tuple.Create(start,dimension));
        }
    }
    internal static class DimensionReduction
    {
        private sealed class Constraint
        {
            public SwView View; public DisplayDimension Display; public string Name,Start,End,Group;
            public bool Generated; public double Value; public int ViewOrder;
        }
        private sealed class Node { public string Id; public double Level; }
        private static string Id(ModelDoc2 model,object entity,string prefix)
        { string id=ModelRecognizer.Persist(model,entity); return id==""?null:prefix+id; }
        private static bool Parallel(double[] v,int axis)
        { return v!=null && v.Length>=3 && Math.Abs(Math.Abs(v[axis])-1)<1e-7 && v.Take(3).Where((x,i)=>i!=axis).All(x=>Math.Abs(x)<1e-7); }
        private static Node Plane(ModelDoc2 model,IEnumerable<Face2> faces,int axis,double level)
        {
            foreach(var face in faces.Where(f=>f!=null)) {
                var surface=face.GetSurface() as Surface; if(surface==null || !surface.IsPlane()) continue;
                var p=(double[])surface.PlaneParams;
                if(Parallel(p,axis) && Math.Abs(p[axis+3]-level)<1e-8) {
                    var id=Id(model,face,"plane:"); if(id!=null) return new Node { Id=id,Level=level };
                }
            }
            return null;
        }
        private static Node SourceNode(ModelDoc2 model,object entity,int axis)
        {
            var native=((PartDoc)model).GetCorrespondingEntity(entity); if(native==null) return null;
            var vertex=native as Vertex;
            if(vertex!=null) {
                double level=((double[])vertex.GetPoint())[axis];
                var faces=(vertex.GetEdges() as object[] ?? new object[0]).Cast<Edge>().SelectMany(e=>(e.GetTwoAdjacentFaces2() as object[] ?? new object[0]).Cast<Face2>());
                return Plane(model,faces,axis,level) ?? new Node { Id=Id(model,vertex,"vertex:"),Level=level };
            }
            var edge=native as Edge; if(edge==null) return null;
            var curve=edge.GetCurve() as Curve; if(curve==null) return null;
            var adjacent=(edge.GetTwoAdjacentFaces2() as object[] ?? new object[0]).Cast<Face2>().ToList();
            if(curve.IsCircle()) {
                var p=(double[])curve.CircleParams; var normal=p.Skip(3).Take(3).ToArray();
                if(Parallel(normal,axis)) return Plane(model,adjacent,axis,p[axis]) ?? new Node { Id=Id(model,edge,"rim:"),Level=p[axis] };
                if(Math.Abs(normal[axis])<1e-7) return new Node { Id=Id(model,edge,"center:"),Level=p[axis] };
                return null;
            }
            if(curve.IsLine()) {
                var p=(double[])curve.LineParams;
                if(Math.Abs(p[axis+3])<1e-7) return Plane(model,adjacent,axis,p[axis]) ?? new Node { Id=Id(model,edge,"line:"),Level=p[axis] };
            }
            return null;
        }
        private static bool Plain(DisplayDimension display)
        {
            return display.IsReferenceDim() && display.ShowDimensionValue && display.GetDimension2(0).GetToleranceType()==0 &&
                Enumerable.Range(1,4).All(i=>string.IsNullOrEmpty(display.GetText(i)));
        }
        internal static void Reduce(ModelDoc2 doc,List<SwView> views,HashSet<string> generated,JobReport report,Action<string> progress)
        {
            var constraints=new List<Constraint>(); var cache=new Dictionary<string,Node>(); int scanned=0;
            for(int vi=0;vi<Math.Min(3,views.Count);vi++) {
                var view=views[vi]; var model=view.ReferencedDocument;
                if(model==null || model.GetType()!=1 || model.ConfigurationManager.ActiveConfiguration.Name!=view.ReferencedConfiguration) continue;
                for(var display=view.GetFirstDisplayDimension5();display!=null;display=display.GetNext5()) {
                    if(display.Type2!=2 || !Plain(display) || ((Annotation)display.GetAnnotation()).IsDangling()) continue;
                    var d=display.GetDimension2(0); if(d.DimensionLineDirection==null) continue;
                    var direction=(double[])d.DimensionLineDirection.ArrayData; int axis=Array.FindIndex(direction,x=>Math.Abs(x)>1-1e-7);
                    if(axis<0 || axis>2 || !Parallel(direction,axis)) continue;
                    var points=d.ReferencePoints as object[]; if(points==null || points.Length<2) continue;
                    double a=((double[])((MathPoint)points[0]).ArrayData)[axis],b=((double[])((MathPoint)points[1]).ArrayData)[axis];
                    if(Math.Abs(Math.Abs(a-b)-d.SystemValue)>1e-8 || Math.Abs(a-b)<1e-8) continue;
                    if(++scanned%10==1) progress("重复标注分析："+scanned+" · "+view.GetName2());
                    var nodes=new List<Node>();
                    foreach(var entity in ((Annotation)display.GetAnnotation()).GetAttachedEntities3() as object[] ?? new object[0]) {
                        string id=ModelRecognizer.PersistFromView(model,entity); if(id=="") continue;
                        string key=view.GetReferencedModelName()+"|"+view.ReferencedConfiguration+"|"+axis+"|"+id; Node node;
                        if(!cache.TryGetValue(key,out node)) {
                            try { node=SourceNode(model,entity,axis); }
                            catch(COMException) { node=null; } // Unknown geometry is retained.
                            cache[key]=node;
                        }
                        if(node!=null && node.Id!=null) nodes.Add(node);
                    }
                    var start=nodes.Where(n=>Math.Abs(n.Level-a)<1e-8).Select(n=>n.Id).Distinct().ToList();
                    var end=nodes.Where(n=>Math.Abs(n.Level-b)<1e-8).Select(n=>n.Id).Distinct().ToList();
                    if(start.Count!=1 || end.Count!=1 || start[0]==end[0]) continue;
                    constraints.Add(new Constraint { View=view,Display=display,Name=d.FullName,Start=start[0],End=end[0],Value=d.SystemValue,ViewOrder=vi,
                        Generated=generated.Contains(view.GetName2()+"|"+d.FullName),Group=view.GetReferencedModelName()+"|"+view.ReferencedConfiguration+"|"+axis });
                }
            }
            foreach(var group in constraints.GroupBy(c=>c.Group)) {
                var graph=new DimensionConstraintGraph();
                // Imported dimensions stay; prefer the primary view and long spans among generated dimensions.
                foreach(var c in group.OrderBy(c=>c.Generated?1:0).ThenBy(c=>c.ViewOrder).ThenByDescending(c=>c.Value)) {
                    var path=graph.Path(c.Start,c.End);
                    if(!c.Generated || path==null) { graph.Add(c.Start,c.End,c.View.GetName2()+"|"+c.Name); continue; }
                    doc.ClearSelection2(true); var annotation=(Annotation)c.Display.GetAnnotation();
                    if(!annotation.Select3(false,null) || !doc.Extension.DeleteSelection2(0)) { report.Warnings.Add("冗余尺寸删除失败："+c.Name); graph.Add(c.Start,c.End,c.View.GetName2()+"|"+c.Name); continue; }
                    report.RemovedDuplicates++;
                    report.RemovedRedundantDimensions.Add(new RemovedRedundantDimension { View=c.View.GetName2(),Dimension=c.Name,ValueSI=c.Value,Start=c.Start,End=c.End,Reason=path.Count==1?"same_geometry":"closed_chain",RetainedDimensions=path });
                }
            }
            ReduceCircularWidths(doc,views,generated,report);
            doc.ClearSelection2(true); progress("重复标注精简完成："+report.RemovedRedundantDimensions.Count+" 项");
        }
        private static void ReduceCircularWidths(ModelDoc2 doc,List<SwView> views,HashSet<string> generated,JobReport report)
        {
            var candidates=new List<Constraint>();
            for(int vi=0;vi<Math.Min(3,views.Count);vi++) {
                var view=views[vi]; var model=view.ReferencedDocument;
                if(model==null || model.GetType()!=1 || model.ConfigurationManager.ActiveConfiguration.Name!=view.ReferencedConfiguration) continue;
                for(var display=view.GetFirstDisplayDimension5();display!=null;display=display.GetNext5()) {
                    if(display.Type2!=2 && display.Type2!=6) continue;
                    var d=display.GetDimension2(0); var ann=(Annotation)display.GetAnnotation();
                    if(!display.IsReferenceDim() || !display.ShowDimensionValue || d.GetToleranceType()!=0 || ann.IsDangling()) continue;
                    if(Enumerable.Range(1,4).Any(i=>!string.IsNullOrEmpty(display.GetText(i)) && !(i==1 && display.GetText(i)=="<MOD-DIAM>"))) continue;
                    var attached=ann.GetAttachedEntities3() as object[] ?? new object[0]; if(attached.Length!=1) continue;
                    try {
                        var edge=((PartDoc)model).GetCorrespondingEntity(attached[0]) as Edge; if(edge==null) continue;
                        var curve=edge.GetCurve() as Curve; if(curve==null || !curve.IsCircle()) continue;
                        var p=(double[])curve.CircleParams; double diameter=p[6]*2;
                        if(Math.Abs(d.SystemValue-diameter)>1e-8) continue;
                        if(display.Type2==2) {
                            var points=d.ReferencePoints as object[]; if(points==null || points.Length<2 || d.DimensionLineDirection==null) continue;
                            var dir=(double[])d.DimensionLineDirection.ArrayData; int axis=Array.FindIndex(dir,x=>Math.Abs(x)>1-1e-7);
                            if(axis<0 || axis>2 || !Parallel(dir,axis) || Math.Abs(p[axis+3])>1e-7) continue;
                            double a=((double[])((MathPoint)points[0]).ArrayData)[axis],b=((double[])((MathPoint)points[1]).ArrayData)[axis];
                            if(Math.Abs(Math.Min(a,b)-(p[axis]-p[6]))>1e-8 || Math.Abs(Math.Max(a,b)-(p[axis]+p[6]))>1e-8) continue;
                        }
                        string identity=Id(model,edge,"circle:");
                        foreach(Face2 face in edge.GetTwoAdjacentFaces2() as object[] ?? new object[0]) {
                            if(face==null) continue; var surface=face.GetSurface() as Surface; if(surface==null || !surface.IsCylinder()) continue;
                            var cylinder=(double[])surface.CylinderParams;
                            if(Math.Abs(cylinder[6]-p[6])<1e-8) { identity=Id(model,face,"cylinder:"); break; }
                        }
                        if(identity==null) continue;
                        candidates.Add(new Constraint { View=view,Display=display,Name=d.FullName,Start=identity,End="diameter",Group=view.GetReferencedModelName()+"|"+view.ReferencedConfiguration+"|"+identity,Generated=generated.Contains(view.GetName2()+"|"+d.FullName),Value=d.SystemValue,ViewOrder=vi });
                    } catch(COMException) { /* Unproved circular geometry is retained. */ }
                }
            }
            foreach(var group in candidates.GroupBy(c=>c.Group)) {
                var ordered=group.OrderBy(c=>c.Generated?1:0).ThenBy(c=>c.Display.Type2==6?0:1).ThenBy(c=>c.ViewOrder).ToList();
                var keeper=ordered[0];
                foreach(var extra in ordered.Skip(1).Where(c=>c.Generated)) {
                    doc.ClearSelection2(true); var ann=(Annotation)extra.Display.GetAnnotation();
                    if(!ann.Select3(false,null) || !doc.Extension.DeleteSelection2(0)) { report.Warnings.Add("圆形重复尺寸删除失败："+extra.Name); continue; }
                    report.RemovedDuplicates++;
                    report.RemovedRedundantDimensions.Add(new RemovedRedundantDimension { View=extra.View.GetName2(),Dimension=extra.Name,ValueSI=extra.Value,Reason="same_geometry",Start=extra.Start,End=extra.End,RetainedDimensions=new List<string> { keeper.View.GetName2()+"|"+keeper.Name } });
                }
            }
        }
    }
}
