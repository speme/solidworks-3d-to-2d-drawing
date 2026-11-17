using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SwView=SolidWorks.Interop.sldworks.View;

namespace AutoDrawing
{
    public sealed class RackHoleEvidence
    {
        public double FromStartMm { get; set; }
        public double HeightDatumMm { get; set; }
        public string Specification { get; set; }
        public List<string> Entities { get; set; }
    }
    public sealed class RackTableEvidence
    {
        public List<string[]> Cells { get; set; }
        public bool Reopened { get; set; }
    }
    public sealed class RackEvidence
    {
        public GearingResult Calculation { get; set; }
        public RackDatumEvidence StartDatum { get; set; }
        public string HoleTableAssociativity { get; set; }
        public Dictionary<string,string> Parameters { get; set; }
        public Dictionary<string,string> Sources { get; set; }
        public RackGeometryEvidence Geometry { get; set; }
        public List<RackHoleEvidence> Holes { get; set; }
        public List<RackTableEvidence> Tables { get; set; }
        public string Status { get; set; }
        public double IsometricScale { get; set; }
        public bool Reopened { get; set; }
        public RackEvidence() { Parameters=new Dictionary<string,string>(); Sources=new Dictionary<string,string>(); Holes=new List<RackHoleEvidence>(); Tables=new List<RackTableEvidence>(); }
    }
    public sealed class RackDatumEvidence
    {
        public string End { get; set; }
        public string View { get; set; }
        public string Text { get; set; }
        public string AnnotationName { get; set; }
        public string Entity { get; set; }
        public double[] ModelPoint { get; set; }
        public bool Reopened { get; set; }
    }
    internal static class RackDrawing
    {
        private static string N(double n) { return n.ToString("0.###",CultureInfo.InvariantCulture); }
        internal static RackEvidence Read(ModelDoc2 model,DrawingOptions options,RecognitionReport recognition)
        {
            if(recognition.RackGeometry==null) return null;
            var result=new RackEvidence { Geometry=recognition.RackGeometry,HoleTableAssociativity="snapshot_at_generation" };
            var aliases=new Dictionary<string,string[]> {
                {"Module",new[]{"RackModule","Module","ModuleMm","模数"}}, {"PressureAngle",new[]{"RackPressureAngle","PressureAngle","压力角"}},
                {"PitchLineFromBack",new[]{"PitchLineFromBack","分度线距背面"}}, {"HelixAngle",new[]{"HelixAngle","螺旋角"}},
                {"Accuracy",new[]{"RackAccuracy","精度等级"}}, {"Material",new[]{"Material","材料"}}, {"HeatTreatment",new[]{"HeatTreatment","热处理"}}
            };
            foreach(string config in new[]{model.ConfigurationManager.ActiveConfiguration.Name,""}) {
                var manager=model.Extension.get_CustomPropertyManager(config); var names=manager.GetNames() as string[] ?? new string[0];
                foreach(var alias in aliases) {
                    if(result.Parameters.ContainsKey(alias.Key)) continue;
                    var name=names.FirstOrDefault(n=>alias.Value.Any(a=>a.Equals(n,StringComparison.OrdinalIgnoreCase))); if(name==null) continue;
                    string raw,resolved; bool wasResolved,linked; manager.Get6(name,false,out raw,out resolved,out wasResolved,out linked);
                    string value=string.IsNullOrWhiteSpace(resolved)?raw:resolved; if(string.IsNullOrWhiteSpace(value)) continue;
                    if(alias.Key=="Material" && (value.Contains("未指定") || value.IndexOf("not specified",StringComparison.OrdinalIgnoreCase)>=0)) continue;
                    result.Parameters[alias.Key]=value.Trim(); result.Sources[alias.Key]="model_property:"+config+":"+name;
                }
            }
            foreach(var p in options.RackParameters) { result.Parameters[p.Key]=p.Value; result.Sources[p.Key]="request.RackParameters"; }
            var inference=options.GearInference!=null && options.GearInference.Family=="rack_straight"?new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<GearingInput>(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(options.GearInference)):new GearingInput { Family="rack_straight" };
            if(!inference.Measurements.ContainsKey("CircularPitchMm")) { inference.Measurements["CircularPitchMm"]=result.Geometry.MeasuredPitchMm; inference.Evidence["CircularPitchMm"]="model_geometry:paired_periodic_planar_flanks"; }
            if(!inference.Measurements.ContainsKey("RackFlankSlope")) { inference.Measurements["RackFlankSlope"]=result.Geometry.MeasuredFlankSlope; inference.Evidence["RackFlankSlope"]="model_geometry:dominant_opposite_flank_normals"; }
            result.Calculation=GearingCalculator.Calculate(inference);
            if(result.Calculation.Status=="conflict") throw new InvalidOperationException("齿条原图/几何约束冲突："+string.Join("；",result.Calculation.Issues));
            foreach(var p in new Dictionary<string,string>{{"Module","ModuleMm"},{"PressureAngle","PressureAngleDeg"}}) if(result.Parameters.ContainsKey(p.Key) && result.Calculation.Values.ContainsKey(p.Value)) {
                double declared; if(double.TryParse(result.Parameters[p.Key],NumberStyles.Float,CultureInfo.InvariantCulture,out declared) && Math.Abs(declared-result.Calculation.Values[p.Value])>Math.Max(1e-5,Math.Abs(declared)*inference.RelativeTolerance)) throw new InvalidOperationException("齿条已给参数与原图/几何推算不一致："+p.Key);
            }
            foreach(var p in new Dictionary<string,string>{{"Module","ModuleMm"},{"PressureAngle","PressureAngleDeg"}}) if(!result.Parameters.ContainsKey(p.Key) && result.Calculation.Values.ContainsKey(p.Value)) {
                double value=result.Calculation.Values[p.Value];
                if(p.Key=="PressureAngle" && value>=45) continue;
                result.Parameters[p.Key]=value.ToString("G10",CultureInfo.InvariantCulture); result.Sources[p.Key]="formula:"+p.Value;
            }
            RackRules.Validate(result.Parameters);
            result.Status=result.Parameters.ContainsKey("Module") && result.Parameters.ContainsKey("PressureAngle")?"review_required":"rack_parameters_required";
            result.Sources["Detection"]=recognition.RackGeometry.Source;
            var g=result.Geometry; var bounds=recognition.BoundsMm;
            foreach(var group in recognition.Geometry.Where(h=>h.Kind=="hole" && h.Axis!=null && Math.Abs(h.Axis[g.WidthAxis])>.999999).GroupBy(RecognitionRules.AxisLine)) {
                var cylinders=group.OrderBy(h=>h.DiameterMm).ToList(); var first=cylinders[0];
                var cones=recognition.Geometry.Where(h=>h.Kind=="cone" && h.Axis!=null && h.AxialMinMm.HasValue && RecognitionRules.AxisLine(h)==group.Key).ToList();
                var intervals=cylinders.Concat(cones).Where(h=>h.AxialMinMm.HasValue).OrderBy(h=>h.AxialMinMm).ToList();
                double reached=bounds[g.WidthAxis]; bool continuous=true;
                foreach(var h in intervals) { if(h.AxialMinMm.Value>reached+1e-4) { continuous=false; break; } reached=Math.Max(reached,h.AxialMaxMm.Value); }
                bool through=continuous && reached>=bounds[g.WidthAxis+3]-1e-4;
                string spec="Φ"+N(first.DiameterMm)+(through?"贯通":"（深度待复核）");
                var large=cylinders.Last();
                if(large.DiameterMm-first.DiameterMm>1e-4 && large.AxialMinMm.HasValue) {
                    double low=large.AxialMinMm.Value,high=large.AxialMaxMm.Value;
                    foreach(var cone in cones.Where(c=>c.SourceFeatureType=="Chamfer" && (Math.Abs(c.MinimumDiameterMm-large.DiameterMm)<1e-4 || Math.Abs(c.DiameterMm-large.DiameterMm)<1e-4))) {
                        if(cone.AxialMinMm.Value<=high+1e-4 && cone.AxialMaxMm.Value>=low-1e-4) { low=Math.Min(low,cone.AxialMinMm.Value); high=Math.Max(high,cone.AxialMaxMm.Value); }
                    }
                    bool atEnd=Math.Abs(low-bounds[g.WidthAxis])<1e-4 || Math.Abs(high-bounds[g.WidthAxis+3])<1e-4;
                    spec+=atEnd?"；沉孔Φ"+N(large.DiameterMm)+"深"+N(high-low):"；内台阶Φ"+N(large.DiameterMm)+"长"+N(high-low)+"待剖视";
                }
                result.Holes.Add(new RackHoleEvidence { FromStartMm=StartMaximum(result)?bounds[g.LengthAxis+3]-first.Center[g.LengthAxis]*1000:first.Center[g.LengthAxis]*1000-bounds[g.LengthAxis],HeightDatumMm=g.BackFaceConfirmed && g.BackAtMaximum?bounds[g.HeightAxis+3]-first.Center[g.HeightAxis]*1000:first.Center[g.HeightAxis]*1000-bounds[g.HeightAxis],Specification=spec,Entities=cylinders.Concat(cones).Select(h=>h.Id).ToList() });
            }
            result.Holes=result.Holes.OrderBy(h=>h.FromStartMm).ThenBy(h=>h.HeightDatumMm).ToList(); return result;
        }
        internal static Box[] Cells(double width,double height,DrawingOptions options)
        {
            double x=options.LeftMarginMm/1000,y=options.TitleBlockHeightMm/1000,top=height-options.TopMarginMm/1000,w=width-x-options.RightMarginMm/1000;
            double viewHeight=Math.Min(.035,(top-y)*.26),endWidth=.035;
            if(top-y<.115 || w<.22) throw new InvalidOperationException("齿条视图与孔表需要更大的可用区域，请减小边距/标题栏预留或使用 A3 横向模板。");
            var cells=new[]{new Box(x,top-viewHeight,w-endWidth-.007,viewHeight),new Box(x,top-2*viewHeight-.012,w-endWidth-.007,viewHeight),new Box(x+w-endWidth,top-viewHeight,endWidth,viewHeight),new Box(x+w-endWidth,top-2*viewHeight-.012,endWidth,viewHeight)};
            return options.FirstAngle?cells:new[]{cells[1],cells[0],cells[3],cells[2]};
        }
        private sealed class V { public Vertex Vertex; public double[] Point; }
        private static bool StartMaximum(RackEvidence rack) { string end; return rack.Parameters.TryGetValue("StartEnd",out end) && end=="Maximum"; }
        internal static void Dimensions(ModelDoc2 doc,List<SwView> views,JobReport report,Action<string> progress)
        {
            var model=views[0].ReferencedDocument; var body=(Body2)((object[])((PartDoc)model).GetBodies2(0,false))[0];
            var vertices=new List<V>(); int count=0;
            foreach(Vertex vertex in body.GetVertices() as object[] ?? new object[0]) {
                if(count++%100==0) progress("齿条外形定位：已读取 "+count+" 个顶点");
                vertices.Add(new V { Vertex=vertex,Point=(double[])vertex.GetPoint() });
            }
            var g=report.Rack.Geometry;
            foreach(int axis in new[]{g.LengthAxis,g.HeightAxis,g.WidthAxis}) {
                var order=axis==g.WidthAxis?views.Skip(1).Take(2).Concat(views.Take(1)):views.Take(3);
                var view=order.FirstOrDefault(v=> { var data=(double[])v.ModelToViewTransform.ArrayData; return Math.Abs(data[axis*3])>.999999 || Math.Abs(data[axis*3+1])>.999999; });
                if(view==null) throw new InvalidOperationException("齿条测量方向不在正投影视图中。");
                var t=(double[])view.ModelToViewTransform.ArrayData;
                bool horizontal=Math.Abs(t[axis*3])>.999999; bool vertical=Math.Abs(t[axis*3+1])>.999999;
                if(!horizontal && !vertical) throw new InvalidOperationException("齿条外形尺寸方向无法映射到正投影视图。");
                var a=vertices.OrderBy(v=>v.Point[axis]).First(); var b=vertices.OrderByDescending(v=>v.Point[axis]).First();
                double expected=b.Point[axis]-a.Point[axis]; var box=(double[])view.GetOutline();
                doc.ClearSelection2(true); ((DrawingDoc)doc).ActivateView(view.GetName2());
                if(!view.SelectEntity(a.Vertex,false) || !view.SelectEntity(b.Vertex,true)) throw new InvalidOperationException("齿条外形顶点关联失败。");
                var d=(horizontal?doc.AddHorizontalDimension2((box[0]+box[2])/2,box[1]-.007,0):doc.AddVerticalDimension2(box[2]+.008,(box[1]+box[3])/2,0)) as DisplayDimension;
                if(d==null || ((Annotation)d.GetAnnotation()).IsDangling() || Math.Abs(d.GetDimension2(0).SystemValue-expected)>1e-7) throw new InvalidOperationException("齿条外形尺寸回读失败。");
            }
            var main=views[0]; var transform=(double[])main.ModelToViewTransform.ArrayData; var outline=(double[])main.GetOutline();
            bool maximum=StartMaximum(report.Rack); var start=maximum?vertices.OrderByDescending(v=>v.Point[g.LengthAxis]).First():vertices.OrderBy(v=>v.Point[g.LengthAxis]).First();
            double dx=transform[g.LengthAxis*3]*(maximum?-1:1),dy=transform[g.LengthAxis*3+1]*(maximum?-1:1);
            string direction=Math.Abs(dx)>=Math.Abs(dy)?dx>0?"向右":"向左":dy>0?"向上":"向下";
            string text="起端 S (X=0), X+"+direction;
            doc.ClearSelection2(true); ((DrawingDoc)doc).ActivateView(main.GetName2());
            if(!main.SelectEntity(start.Vertex,false)) throw new InvalidOperationException("齿条起端实体关联失败。");
            var note=doc.InsertNote(text) as Note; if(note==null) throw new InvalidOperationException("齿条起端标识创建失败。");
            var annotation=(Annotation)note.GetAnnotation(); annotation.SetLeader3(1,0,true,false,false,false);
            if(!annotation.SetPosition2(dx>=0?outline[0]+.012:outline[2]-.045,outline[3]+.003,0)) throw new InvalidOperationException("齿条起端标识定位失败。");
            var ids=(annotation.GetAttachedEntities3() as object[] ?? new object[0]).Select(e=>ModelRecognizer.PersistFromView(model,e)).ToList(); string target=ModelRecognizer.Persist(model,start.Vertex);
            if(!ids.Contains(target)) throw new InvalidOperationException("齿条起端标识未关联实际端点。");
            report.Rack.StartDatum=new RackDatumEvidence { End=maximum?"Maximum":"Minimum",View=main.GetName2(),Text=note.GetText(),AnnotationName=annotation.GetName(),Entity=target,ModelPoint=start.Point };
            doc.ClearSelection2(true); ((DrawingDoc)doc).ActivateView("");
        }
        private static string Value(RackEvidence rack,string key) { string value; return rack.Parameters.TryGetValue(key,out value)?value:"未提供"; }
        internal static void Tables(ModelDoc2 doc,DrawingOptions options,JobReport report)
        {
            var r=report.Rack; var b=report.Recognition.BoundsMm; var g=r.Geometry;
            double x=options.LeftMarginMm/1000,y=options.TitleBlockHeightMm/1000,top=report.SheetSizeMm[1]/1000-options.TopMarginMm/1000;
            double tableTop=top-2*Math.Min(.035,(top-y)*.26)-.018,w=report.SheetSizeMm[0]/1000-x-options.RightMarginMm/1000;
            var parameters=new List<string[]> { new[]{r.Status=="rack_parameters_required"?"齿条参数待补":"齿条参数","值"},new[]{"外形 长×宽×高",N(b[g.LengthAxis+3]-b[g.LengthAxis])+"×"+N(b[g.WidthAxis+3]-b[g.WidthAxis])+"×"+N(b[g.HeightAxis+3]-b[g.HeightAxis])},new[]{"实测重复节距 mm",N(g.MeasuredPitchMm)},new[]{"模数 m",Value(r,"Module")},new[]{"压力角 °",Value(r,"PressureAngle")},new[]{"分度线距背面",Value(r,"PitchLineFromBack")},new[]{"精度等级",Value(r,"Accuracy")} };
            if(r.Sources.ContainsKey("Module") && r.Sources["Module"].StartsWith("formula:")) parameters[3][0]="模数 m（计算）";
            if(r.Sources.ContainsKey("PressureAngle") && r.Sources["PressureAngle"].StartsWith("formula:")) parameters[4][0]="压力角 °（计算）";
            if(options.Isometric) parameters.Add(new[]{"轴测比例","1:"+N(1/r.IsometricScale)});
            foreach(string key in new[]{"HelixAngle","Material","HeatTreatment"}) if(r.Parameters.ContainsKey(key)) parameters.Add(new[]{key,Value(r,key)});
            InsertTable(doc,parameters,x,tableTop,new[]{w*.19,w*.19},y,report);
            var holes=new List<string[]> { new[]{"孔号","距起端 S",g.BackFaceConfirmed?"距背面":"高向距最小面","孔规格 mm"} };
            for(int i=0;i<r.Holes.Count;i++) { var h=r.Holes[i]; holes.Add(new[]{(i+1).ToString(),N(h.FromStartMm),N(h.HeightDatumMm),h.Specification}); }
            if(r.Holes.Count==0) holes.Add(new[]{"—","—","—","安装孔需补充"});
            InsertTable(doc,holes,x+w*.40,tableTop,new[]{w*.045,w*.13,w*.13,w*.295},y,report);
            report.Warnings.Add("齿条专项出图：跳过逐齿模型尺寸导入、全视图自动标注及齿部倒角/圆角泛化标注；孔表基准为模型长向最小端和确认的背面（未确认则为高向最小面）。齿形、倒角、公差及局部放大图需复核。");
            report.Warnings.Add("齿条孔表为本次生成时的几何测量快照，模型改动后需重新生成；轴测视图采用独立比例，主投影视图采用图纸比例。");
            if(r.Status=="rack_parameters_required") report.Warnings.Add("齿条制造参数约束不足，请通过齿条参数或原图测量补充。");
            if(r.Sources.Values.Any(s=>s.StartsWith("formula:"))) report.Warnings.Add("齿条模数/压力角含公式推算值，依据周期节距和直齿侧面法向；未按标准模数系列强制舍入，须与原图齿制及公差核对。");
        }
        private static void InsertTable(ModelDoc2 doc,List<string[]> cells,double x,double y,double[] widths,double bottom,JobReport report)
        {
            if(cells.Count*.0045>y-bottom-.002) throw new InvalidOperationException("齿条参数/孔表超出可用区域，请使用更大模板。");
            var table=((DrawingDoc)doc).InsertTableAnnotation2(false,x,y,1,"",cells.Count,widths.Length);
            if(table==null) throw new InvalidOperationException("齿条参数/孔表创建失败。");
            table.TitleVisible=false; var font=table.GetTextFormat(); font.TypeFaceName="宋体"; font.CharHeight=.0022; table.SetTextFormat(false,font);
            for(int row=0;row<cells.Count;row++) { for(int col=0;col<widths.Length;col++) { table.set_Text(row,col,cells[row][col]); table.SetCellTextFormat(row,col,false,font); } table.SetRowVerticalGap(row,.0003); table.SetRowHeight(row,.0045,0); }
            for(int col=0;col<widths.Length;col++) table.SetColumnWidth(col,widths[col],0);
            report.Rack.Tables.Add(new RackTableEvidence { Cells=cells });
        }
        internal static void Verify(DrawingDoc drawing,JobReport report)
        {
            if(report.Rack==null) return;
            foreach(var expected in report.Rack.Tables) {
                bool found=false;
                for(var view=drawing.GetFirstView() as SwView;view!=null;view=view.GetNextView() as SwView)
                for(var table=view.GetFirstTableAnnotation() as TableAnnotation;table!=null;table=table.GetNext() as TableAnnotation) {
                    if(table.RowCount!=expected.Cells.Count || table.ColumnCount!=expected.Cells[0].Length) continue;
                    bool match=true; for(int row=0;row<table.RowCount;row++) for(int col=0;col<table.ColumnCount;col++) if(table.get_Text(row,col)!=expected.Cells[row][col]) match=false;
                    if(match) { found=true; break; }
                }
                if(!found) throw new InvalidOperationException("齿条参数/孔表保存重开核验失败。"); expected.Reopened=true;
            }
            report.Rack.Reopened=true;
            if(report.Rack.StartDatum!=null) {
                var expected=report.Rack.StartDatum; SwView view=((SwView)drawing.GetFirstView()).GetNextView() as SwView;
                while(view!=null && view.GetName2()!=expected.View) view=view.GetNextView() as SwView;
                var note=view==null?null:(view.GetNotes() as object[] ?? new object[0]).OfType<Note>().FirstOrDefault(n=>((Annotation)n.GetAnnotation()).GetName()==expected.AnnotationName);
                if(note==null || note.GetText()!=expected.Text || !(((Annotation)note.GetAnnotation()).GetAttachedEntities3() as object[] ?? new object[0]).Any(e=>ModelRecognizer.PersistFromView(view.ReferencedDocument,e)==expected.Entity)) throw new InvalidOperationException("起端标识保存重开后的文字/实体关联不一致。");
                expected.Reopened=true;
            }
        }
    }
}
