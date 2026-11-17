using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Runtime.CompilerServices;
using SolidWorks.Interop.sldworks;
using SwView=SolidWorks.Interop.sldworks.View;

namespace AutoDrawing
{
    public sealed class PaperLine
    {
        public double X1,Y1,X2,Y2;
        public PaperLine(double x1,double y1,double x2,double y2) { X1=x1;Y1=y1;X2=x2;Y2=y2; }
        public double Length { get { return Math.Sqrt((X2-X1)*(X2-X1)+(Y2-Y1)*(Y2-Y1)); } }
    }
    public static class LineRules
    {
        private static double Cross(double ax,double ay,double bx,double by) { return ax*by-ay*bx; }
        // Positive means a real shared length; touching endpoints and nearby parallel lines are excluded.
        public static double Overlap(PaperLine a,PaperLine b)
        {
            if(a.Length<1e-8 || b.Length<1e-8) return 0;
            double ux=(a.X2-a.X1)/a.Length,uy=(a.Y2-a.Y1)/a.Length;
            if(Math.Abs(Cross(ux,uy,b.X2-b.X1,b.Y2-b.Y1))>1e-6 || Math.Abs(Cross(ux,uy,b.X1-a.X1,b.Y1-a.Y1))>1e-6) return 0;
            double t1=(b.X1-a.X1)*ux+(b.Y1-a.Y1)*uy,t2=(b.X2-a.X1)*ux+(b.Y2-a.Y1)*uy;
            return Math.Max(0,Math.Min(a.Length,Math.Max(t1,t2))-Math.Max(0,Math.Min(t1,t2)));
        }
        public static bool Crosses(PaperLine a,PaperLine b)
        {
            double dx=a.X2-a.X1,dy=a.Y2-a.Y1,ex=b.X2-b.X1,ey=b.Y2-b.Y1,d=Cross(dx,dy,ex,ey);
            if(Math.Abs(d)<1e-12) return false;
            double t=Cross(b.X1-a.X1,b.Y1-a.Y1,ex,ey)/d,u=Cross(b.X1-a.X1,b.Y1-a.Y1,dx,dy)/d;
            double at=.0004/Math.Max(a.Length,.0004),bt=.0004/Math.Max(b.Length,.0004);
            return t>at && t<1-at && u>bt && u<1-bt;
        }
    }
    public sealed class AnnotationLayoutEvidence
    {
        public int DimensionCount { get; set; }
        public int Moved { get; set; }
        public int SharedWitnessPairsBefore { get; set; }
        public int SharedWitnessPairsAfter { get; set; }
        public int LineOverlapsBefore { get; set; }
        public int LineOverlapsAfter { get; set; }
        public int GeometryOverlapsBefore { get; set; }
        public int GeometryOverlapsAfter { get; set; }
        public int CrossingsBefore { get; set; }
        public int CrossingsAfter { get; set; }
        public string Layer { get; set; }
        public bool Reopened { get; set; }
        public bool GeometryReusedOnReopen { get; set; }
    }
    internal static class AnnotationLayout
    {
        internal const string LayerName="AUTO_DIMENSIONS";
        private sealed class GeometrySnapshot { public string Signature; public List<PaperLine> Lines; }
        private static readonly ConditionalWeakTable<JobReport,GeometrySnapshot> snapshots=new ConditionalWeakTable<JobReport,GeometrySnapshot>();
        private static string Signature(List<SwView> views)
        {
            return string.Join("|",views.Select(v=> {
                string path=v.GetReferencedModelName(); var info=new FileInfo(path);
                return path+"|"+v.ReferencedConfiguration+"|"+(info.Exists?info.Length+":"+info.LastWriteTimeUtc.Ticks:"missing")+"|"+v.ReferencedDocument.GetSaveFlag()+"|"+v.ReferencedDocument.GetUpdateStamp()+"|"+string.Join(",",((double[])v.ModelToViewTransform.ArrayData).Select(x=>x.ToString("R",System.Globalization.CultureInfo.InvariantCulture)));
            }));
        }
        internal static void Release(JobReport report) { snapshots.Remove(report); }
        private sealed class Item
        { public SwView View; public DisplayDimension Display; public Annotation Annotation; public List<PaperLine> Lines; public List<Box> Text; public double[] Position; public int OriginalWitness; public int Type; public Box Bounds; public List<string> References; }
        private static double[] Transform(double[] p,double[] t)
        { return new[]{(p[0]*t[0]+p[1]*t[3]+p[2]*t[6])*t[12]+t[9],(p[0]*t[1]+p[1]*t[4]+p[2]*t[7])*t[12]+t[10],0.0}; }
        private static List<PaperLine> Lines(DisplayData data)
        {
            var lines=new List<PaperLine>();
            for(int i=0;i<data.GetLineCount();i++) { var p=data.GetLineAtIndex3(i) as double[]; if(p!=null && p.Length>=10) lines.Add(new PaperLine(p[4],p[5],p[7],p[8])); }
            return lines.Where(l=>l.Length>1e-7).ToList();
        }
        private static void Refresh(Item item)
        {
            var data=(DisplayData)item.Display.GetDisplayData(); item.Lines=Lines(data); item.Text=new List<Box>();
            for(int i=0;i<data.GetTextCount();i++) {
                string text=data.GetTextAtIndex(i); if(string.IsNullOrEmpty(text)) continue;
                var p=(double[])data.GetTextPositionAtIndex(i); double h=data.GetTextHeightAtIndex(i),w=Math.Max(h*.5,text.Length*h*.52),angle=data.GetTextAngleAtIndex(i);
                double dx=Math.Cos(angle)*w,dy=Math.Sin(angle)*w;
                item.Text.Add(new Box(Math.Min(p[0],p[0]+dx)-h*Math.Abs(Math.Sin(angle))-.0003,Math.Min(p[1],p[1]+dy)-.0003,Math.Abs(dx)+h*Math.Abs(Math.Sin(angle))+.0006,Math.Abs(dy)+h*Math.Abs(Math.Cos(angle))+.0006));
            }
            item.Position=(double[])item.Annotation.GetPosition();
        }
        private static List<Item> Items(IEnumerable<SwView> views)
        {
            var items=new List<Item>();
            foreach(var v in views) for(var d=v.GetFirstDisplayDimension5();d!=null;d=d.GetNext5()) { var p=(double[])v.GetOutline(); var item=new Item { View=v,Display=d,Annotation=(Annotation)d.GetAnnotation(),OriginalWitness=d.WitnessVisibility,Type=d.Type2,Bounds=new Box(p[0],p[1],p[2]-p[0],p[3]-p[1]) }; item.References=(item.Annotation.GetAttachedEntities3() as object[] ?? new object[0]).Select(e=>ModelRecognizer.PersistFromView(v.ReferencedDocument,e)).ToList(); Refresh(item); items.Add(item); }
            return items;
        }
        private static List<PaperLine> Geometry(List<SwView> views,Action<string> progress=null)
        {
            var lines=new List<PaperLine>();
            foreach(var view in views) {
                if(progress!=null) progress("读取视图线条："+view.GetName2());
                int edgeIndex=0;
                var t=(double[])view.ModelToViewTransform.ArrayData;
                foreach(Component2 component in view.GetVisibleComponents() as object[] ?? new object[0])
                foreach(Edge edge in view.GetVisibleEntities2(component,1) as object[] ?? new object[0]) {
                    if(progress!=null && edgeIndex++%50==0) progress("线条检查："+view.GetName2()+"，已读取 "+edgeIndex+" 条边");
                    var native=edge;
                    if(!((Curve)edge.GetCurve()).IsLine() && view.ReferencedDocument.GetType()==1) native=((PartDoc)view.ReferencedDocument).GetCorrespondingEntity(edge) as Edge;
                    if(native==null) continue;
                    var curve=(Curve)native.GetCurve(); var parameters=(CurveParamData)native.GetCurveParams3();
                    var start=(double[])parameters.StartPoint; var end=(double[])parameters.EndPoint;
                    var points=curve.IsLine()?start.Concat(end).ToArray():curve.GetTessPts(.0001,.001,start,end) as double[];
                    if(points==null || points.Length<6) continue;
                    double[] previous=null;
                    for(int k=0;k+2<points.Length;k+=3) { var p=Transform(new[]{points[k],points[k+1],points[k+2]},t); if(previous!=null) lines.Add(new PaperLine(previous[0],previous[1],p[0],p[1])); previous=p; }
                }
            }
            return lines.Where(l=>l.Length>1e-7).ToList();
        }
        private static bool Overlap(Box a,Box b) { return a.X<b.X+b.W && b.X<a.X+a.W && a.Y<b.Y+b.H && b.Y<a.Y+a.H; }
        private static bool SharedWitness(Item a,PaperLine x,Item b,PaperLine y)
        {
            if(a.Type!=2 || b.Type!=2 || a.View!=b.View) return false;
            int ai=a.Lines.IndexOf(x),bi=b.Lines.IndexOf(y);
            return ai>=0 && ai<2 && bi>=0 && bi<2 && ai<a.References.Count && bi<b.References.Count && a.References[ai]!="" && a.References[ai]==b.References[bi] && Math.Abs(x.X1-y.X1)<1e-7 && Math.Abs(x.Y1-y.Y1)<1e-7;
        }
        private static bool Through(PaperLine l,Box b)
        {
            if(l.X1>b.X && l.X1<b.X+b.W && l.Y1>b.Y && l.Y1<b.Y+b.H) return true;
            return LineRules.Crosses(l,new PaperLine(b.X,b.Y,b.X+b.W,b.Y)) || LineRules.Crosses(l,new PaperLine(b.X,b.Y+b.H,b.X+b.W,b.Y+b.H)) || LineRules.Crosses(l,new PaperLine(b.X,b.Y,b.X,b.Y+b.H)) || LineRules.Crosses(l,new PaperLine(b.X+b.W,b.Y,b.X+b.W,b.Y+b.H));
        }
        private static double Score(Item item,List<Item> items,List<PaperLine> geometry,Box printable)
        {
            double cost=item.Lines.Sum(l=>l.Length)*500;
            foreach(var text in item.Text) {
                if(!printable.Contains(new[]{text.X,text.Y,text.X+text.W,text.Y+text.H},0)) cost+=100000;
                if(Overlap(text,item.Bounds)) cost+=5000;
                foreach(var g in geometry) if(Through(g,text)) cost+=1200;
            }
            for(int index=0;index<item.Lines.Count;index++) {
                var line=item.Lines[index];
                if(item.Type==2 && index>=2 && Through(line,item.Bounds)) cost+=30000;
                foreach(var g in geometry) { double overlap=LineRules.Overlap(line,g); if(overlap>.0007) cost+=1000000; if(LineRules.Crosses(line,g)) cost+=2000; }
            }
            foreach(var other in items.Where(i=>i!=item)) {
                foreach(var a in item.Text) foreach(var b in other.Text) if(Overlap(a,b)) cost+=30000;
                foreach(var l in item.Lines) {
                    foreach(var b in other.Text) if(Through(l,b)) cost+=1000;
                    foreach(var m in other.Lines) {
                        // Layout heuristic: identical witness origins can share a lane. The audit
                        // below additionally requires the same persistent source entity.
                        bool sharedWitness=item.Type==2 && other.Type==2 && item.Lines.IndexOf(l)<2 && other.Lines.IndexOf(m)<2 && Math.Abs(l.X1-m.X1)<1e-7 && Math.Abs(l.Y1-m.Y1)<1e-7;
                        if(!sharedWitness && LineRules.Overlap(l,m)>.0007) cost+=1000000;
                        if(LineRules.Crosses(l,m)) cost+=35;
                    }
                }
                foreach(var l in other.Lines) foreach(var b in item.Text) if(Through(l,b)) cost+=1000;
            }
            return cost;
        }
        private static int[] Counts(List<Item> items,List<PaperLine> geometry)
        {
            int overlaps=0,model=0,cross=0,shared=0;
            for(int i=0;i<items.Count;i++) {
                foreach(var a in items[i].Lines) foreach(var b in geometry) if(LineRules.Overlap(a,b)>.0007) model++;
                for(int j=i+1;j<items.Count;j++) foreach(var a in items[i].Lines) foreach(var b in items[j].Lines) { if(LineRules.Overlap(a,b)>.0007) { if(SharedWitness(items[i],a,items[j],b)) shared++; else overlaps++; } if(LineRules.Crosses(a,b)) cross++; }
            }
            return new[]{overlaps,model,cross,shared};
        }
        internal static void ApplyStyle(ModelDoc2 doc,JobReport report)
        {
            var extension=doc.Extension;
            extension.SetUserPreferenceInteger(53,0,2); extension.SetUserPreferenceInteger(63,0,0); extension.SetUserPreferenceInteger(517,0,0);
            if(extension.GetUserPreferenceInteger(53,0)!=2 || extension.GetUserPreferenceInteger(63,0)!=0 || extension.GetUserPreferenceInteger(517,0)!=0) throw new InvalidOperationException("轮廓/标注线宽回读失败。");
            extension.SetUserPreferenceDouble(13,0,.0012); extension.SetUserPreferenceDouble(14,0,.0015);
            var manager=(LayerMgr)doc.GetLayerManager(); var layer=manager.GetLayer(LayerName) as Layer;
            if(layer==null) { manager.AddLayer(LayerName,"自动尺寸及引线",0xC06020,0,0); layer=manager.GetLayer(LayerName) as Layer; }
            if(layer==null) throw new InvalidOperationException("标注图层创建失败。");
            layer.Color=0xC06020; layer.Style=0; layer.Width=0;
        }
        internal static void StyleNotes(IEnumerable<SwView> views)
        {
            foreach(var view in views) foreach(Annotation annotation in view.GetAnnotations() as object[] ?? new object[0])
                if(annotation.GetType()==6) { annotation.Layer=LayerName; annotation.LayerOverride=0; }
        }
        internal static void Arrange(ModelDoc2 doc,List<SwView> views,JobReport report,Action<string> progress)
        {
            ApplyStyle(doc,report); var items=Items(views); var evidence=new AnnotationLayoutEvidence { DimensionCount=items.Count,Layer=LayerName };
            report.AnnotationLayout=evidence;
            foreach(var item in items) { item.Annotation.Layer=LayerName; item.Annotation.LayerOverride=0; }
            if(items.Count>150) { report.Warnings.Add("尺寸超过 150 个，保留原生排列；请分图或使用专用特征标注。"); return; }
            var geometry=Geometry(views,progress); snapshots.Remove(report); snapshots.Add(report,new GeometrySnapshot { Signature=Signature(views),Lines=geometry });
            var before=Counts(items,geometry); evidence.LineOverlapsBefore=before[0]; evidence.GeometryOverlapsBefore=before[1]; evidence.CrossingsBefore=before[2]; evidence.SharedWitnessPairsBefore=before[3];
            var o=report.Options; var printable=new Box(o.LeftMarginMm/1000+.003,o.TitleBlockHeightMm/1000+.003,report.SheetSizeMm[0]/1000-(o.LeftMarginMm+o.RightMarginMm)/1000-.006,report.SheetSizeMm[1]/1000-(o.TitleBlockHeightMm+o.TopMarginMm)/1000-.006);
            // Start with short dimensions near their view and overall dimensions on outer lanes.
            foreach(var group in items.Where(i=>i.Type==2).GroupBy(i=>i.View.GetName2())) {
                int horizontal=0,vertical=0;
                foreach(var item in group.OrderBy(i=>i.Display.GetDimension2(0).SystemValue)) {
                    item.Display.CenterText=true;
                    var points=item.Display.GetDimension2(0).ReferencePoints as object[];
                    if(points==null || points.Length<2) continue;
                    var t=(double[])item.View.ModelToViewTransform.ArrayData; var a=Transform((double[])((MathPoint)points[0]).ArrayData,t); var b=Transform((double[])((MathPoint)points[1]).ArrayData,t); var box=(double[])item.View.GetOutline();
                    bool h=Math.Abs(a[0]-b[0])>Math.Abs(a[1]-b[1]); double lane=.007+(h?horizontal++:vertical++)*o.SpacingMm/1000;
                    double x=h?(a[0]+b[0])/2:box[0]-lane,y=h?box[1]-lane:(a[1]+b[1])/2;
                    if(item.Annotation.SetPosition2(x,y,item.Position[2])) Refresh(item);
                }
            }
            for(int pass=0;pass<2;pass++) foreach(var item in items.OrderBy(i=>i.Type==2?0:1)) {
                progress("标注避让：第 "+(pass+1)+" 轮，"+item.Display.GetDimension2(0).FullName);
                var start=item.Position.ToArray(); double best=Score(item,items,geometry,printable),bx=start[0],by=start[1];
                var bounds=(double[])item.View.GetOutline(); double cx=(bounds[0]+bounds[2])/2,cy=(bounds[1]+bounds[3])/2;
                var candidates=new List<double[]>();
                if(item.Type==2) {
                    var direction=item.Display.GetDimension2(0).DimensionLineDirection; var t=(double[])item.View.ModelToViewTransform.ArrayData;
                    if(direction!=null) { var v=(double[])direction.ArrayData; double dx=v[0]*t[0]+v[1]*t[3]+v[2]*t[6],dy=v[0]*t[1]+v[1]*t[4]+v[2]*t[7];
                        for(int k=0;k<3;k++) { double lane=.007+k*o.SpacingMm/1000; if(Math.Abs(dx)>Math.Abs(dy)) { candidates.Add(new[]{start[0],bounds[1]-lane}); candidates.Add(new[]{start[0],bounds[3]+lane}); } else { candidates.Add(new[]{bounds[0]-lane,start[1]}); candidates.Add(new[]{bounds[2]+lane,start[1]}); } }
                    }
                } else if(item.Type==5 || item.Type==6 || item.Type==10) {
                    for(int sx=-1;sx<=1;sx+=2) for(int sy=-1;sy<=1;sy+=2) for(int k=0;k<2;k++) candidates.Add(new[]{cx+sx*((bounds[2]-bounds[0])/2+.010+k*.008),cy+sy*((bounds[3]-bounds[1])/2+.006+k*.006)});
                    foreach(double fraction in new[]{.15,.5,.85}) for(int side=-1;side<=1;side+=2) candidates.Add(new[]{side<0?bounds[0]-.014:bounds[2]+.014,bounds[1]+(bounds[3]-bounds[1])*fraction});
                }
                foreach(var p in candidates) {
                    if(p[0]<printable.X || p[0]>printable.X+printable.W || p[1]<printable.Y || p[1]>printable.Y+printable.H) continue;
                    if(!item.Annotation.SetPosition2(p[0],p[1],start[2])) continue; Refresh(item);
                    double score=Score(item,items,geometry,printable)+Math.Sqrt((p[0]-start[0])*(p[0]-start[0])+(p[1]-start[1])*(p[1]-start[1]))*100;
                    if(score<best-.01) { best=score; bx=p[0]; by=p[1]; }
                }
                item.Annotation.SetPosition2(bx,by,start[2]); Refresh(item);
                if(Math.Abs(bx-start[0])+Math.Abs(by-start[1])>1e-7) evidence.Moved++;
            }
            doc.ClearSelection2(true); ((DrawingDoc)doc).ActivateView(""); if(!doc.ForceRebuild3(false)) throw new InvalidOperationException("标注避让重建失败。");
            foreach(var item in items) Refresh(item);
            doc.GraphicsRedraw2(); if(!doc.ForceRebuild3(false)) throw new InvalidOperationException("共用尺寸界线重建失败。");
            foreach(var item in items) Refresh(item);
            var after=Counts(items,geometry); evidence.LineOverlapsAfter=after[0]; evidence.GeometryOverlapsAfter=after[1]; evidence.CrossingsAfter=after[2]; evidence.SharedWitnessPairsAfter=after[3];
            if(after[0]>0 || after[1]>0) report.Warnings.Add("仍有线段重合需复核：标注间 "+after[0]+"，标注/实体 "+after[1]+"；详见 AnnotationLayout。");
        }
        internal static void Verify(IEnumerable<SwView> views,JobReport report)
        {
            if(report.AnnotationLayout==null) return;
            var list=views.ToList(); var items=Items(list);
            foreach(var item in items) if(item.Annotation.Layer!=LayerName) throw new InvalidOperationException("保存重开后尺寸图层不一致。");
            GeometrySnapshot cached; bool reuse=snapshots.TryGetValue(report,out cached) && cached.Signature==Signature(list);
            var after=Counts(items,reuse?cached.Lines:Geometry(list)); report.AnnotationLayout.LineOverlapsAfter=after[0]; report.AnnotationLayout.GeometryOverlapsAfter=after[1]; report.AnnotationLayout.CrossingsAfter=after[2]; report.AnnotationLayout.SharedWitnessPairsAfter=after[3];
            report.AnnotationLayout.GeometryReusedOnReopen=reuse;
            report.AnnotationLayout.Reopened=true;
        }
    }
}
