using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SwView=SolidWorks.Interop.sldworks.View;

namespace AutoDrawing
{
    public sealed class GearEvidence
    {
        public Dictionary<string,string> Parameters { get; set; }
        public Dictionary<string,string> Sources { get; set; }
        public List<string[]> TableCells { get; set; }
        public double[] TablePosition { get; set; }
        public string Status { get; set; }
        public bool Reopened { get; set; }
        public GearingResult Calculation { get; set; }
        public GearEvidence() { Parameters=new Dictionary<string,string>(); Sources=new Dictionary<string,string>(); TableCells=new List<string[]>(); Status="review_required"; }
    }
    public static class GearRules
    {
        public static readonly string[] Keys={"Module","Teeth","PressureAngle","HelixAngle","ProfileShift","GearType","Accuracy","HeatTreatment","Material","Hand"};
        public static void Validate(Dictionary<string,string> values)
        {
            if(values==null) throw new ArgumentException("GearParameters 不能为 null。");
            foreach(var pair in values) {
                if(!Keys.Contains(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)) throw new ArgumentException("齿轮参数名称或值无效："+pair.Key);
                if(new[]{"Module","Teeth","PressureAngle","HelixAngle","ProfileShift"}.Contains(pair.Key)) {
                    double number; if(!double.TryParse(pair.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out number) || double.IsNaN(number) || double.IsInfinity(number)) throw new ArgumentException("齿轮数值参数无效："+pair.Key);
                    if(pair.Key=="Module" && (number<=0 || number>1000) || pair.Key=="Teeth" && (number<3 || number>10000 || number!=Math.Truncate(number)) || pair.Key=="PressureAngle" && (number<=0 || number>=45) || pair.Key=="HelixAngle" && Math.Abs(number)>=90) throw new ArgumentException("齿轮参数超出范围："+pair.Key);
                }
            }
        }
    }
    internal static class GearDrawing
    {
        private static readonly Dictionary<string,string[]> aliases=new Dictionary<string,string[]> {
            {"Module",new[]{"Module","ModuleMm","模数"}}, {"Teeth",new[]{"Teeth","NumberOfTeeth","Number of Teeth","齿数"}},
            {"PressureAngle",new[]{"PressureAngle","Pressure Angle","压力角"}}, {"HelixAngle",new[]{"HelixAngle","Helix Angle","螺旋角"}},
            {"ProfileShift",new[]{"ProfileShift","变位系数"}}, {"GearType",new[]{"GearType","齿轮类型","齿制"}},
            {"Accuracy",new[]{"GearAccuracy","齿轮精度","精度等级"}}, {"HeatTreatment",new[]{"HeatTreatment","热处理"}},
            {"Material",new[]{"Material","材料"}}, {"Hand",new[]{"Hand","旋向"}}
        };
        internal static GearEvidence Read(ModelDoc2 model,DrawingOptions options,RecognitionReport recognition)
        {
            if(model.GetType()!=1) return null;
            var result=new GearEvidence();
            foreach(string config in new[]{model.ConfigurationManager.ActiveConfiguration.Name,""}) {
                var manager=model.Extension.get_CustomPropertyManager(config);
                var names=manager.GetNames() as string[] ?? new string[0];
                foreach(var alias in aliases) {
                    if(result.Parameters.ContainsKey(alias.Key)) continue;
                    string name=names.FirstOrDefault(n=>alias.Value.Any(a=>a.Equals(n,StringComparison.OrdinalIgnoreCase))); if(name==null) continue;
                    string raw,resolved; bool wasResolved,linked; manager.Get6(name,false,out raw,out resolved,out wasResolved,out linked);
                    string value=string.IsNullOrWhiteSpace(resolved)?raw:resolved; if(string.IsNullOrWhiteSpace(value)) continue;
                    result.Parameters[alias.Key]=value.Trim(); result.Sources[alias.Key]="model_property:"+config+":"+name;
                }
            }
            foreach(var pair in options.GearParameters) { result.Parameters[pair.Key]=pair.Value; result.Sources[pair.Key]="request.GearParameters"; }
            if(options.GearInference!=null && options.GearInference.Family!="rack_straight" && options.GearInference.Family!="worm_pair") {
                if(options.GearInference.Family.StartsWith("helical") && options.GearInference.ModuleSystem!="normal" && options.GearInference.ModuleSystem!="transverse") throw new InvalidOperationException("斜齿轮出图须选择法向或端面参数基面；公式计算可分别给出两者。");
                result.Calculation=GearingCalculator.Calculate(options.GearInference);
                if(result.Calculation.Status=="conflict") throw new InvalidOperationException("齿轮公式输入存在冲突。");
                bool normal=options.GearInference.Family.StartsWith("helical") && options.GearInference.ModuleSystem!="transverse";
                var mapping=new Dictionary<string,string> { {"Module",normal?"NormalModuleMm":options.GearInference.Family.StartsWith("helical")?"TransverseModuleMm":"ModuleMm"},{"Teeth","Teeth"},{"PressureAngle",normal?"NormalPressureAngleDeg":options.GearInference.Family.StartsWith("helical")?"TransversePressureAngleDeg":"PressureAngleDeg"},{"HelixAngle","HelixAngleDeg"} };
                foreach(var p in mapping) if(result.Calculation.Values.ContainsKey(p.Value)) {
                    if(!result.Parameters.ContainsKey(p.Key)) { result.Parameters[p.Key]=result.Calculation.Values[p.Value].ToString("G10",CultureInfo.InvariantCulture); result.Sources[p.Key]="formula:"+p.Value; }
                    else { double supplied; if(double.TryParse(result.Parameters[p.Key],NumberStyles.Float,CultureInfo.InvariantCulture,out supplied) && Math.Abs(supplied-result.Calculation.Values[p.Value])>Math.Max(1e-5,Math.Abs(supplied)*options.GearInference.RelativeTolerance)) throw new InvalidOperationException("齿轮模型/显式参数与原图公式约束冲突："+p.Key); }
                }
                if(!result.Parameters.ContainsKey("GearType")) { result.Parameters["GearType"]=options.GearInference.Family; result.Sources["GearType"]="request.GearInference.Family"; }
                if(!result.Parameters.ContainsKey("Hand") && !string.IsNullOrWhiteSpace(options.GearInference.Hand)) { result.Parameters["Hand"]=options.GearInference.Hand; result.Sources["Hand"]="request.GearInference.Hand"; }
            }
            bool complete=result.Parameters.ContainsKey("Module") && result.Parameters.ContainsKey("Teeth");
            if(!complete) {
                bool declared=new[]{"Module","Teeth","GearType"}.Any(result.Parameters.ContainsKey);
                string title=model.GetTitle().ToLowerInvariant(); bool hint=title.Contains("齿轮") || title.Contains("gear");
                bool excluded=new[]{"定位","夹具","齿轮座","齿轮箱","齿轮罩"}.Any(title.Contains);
                if(!declared && (!hint || excluded)) return null;
                bool radial=CentralBore(recognition)!=null;
                var bodies=((PartDoc)model).GetBodies2(0,false) as object[] ?? new object[0];
                bool detailed=bodies.Length==1 && ((object[])((Body2)bodies[0]).GetEdges()).Length>150;
                if(!declared && (!hint || excluded || !radial || !detailed)) return null;
                result.Status="gear_parameters_required"; result.Sources["Detection"]=declared?"partial_gear_properties":"filename_hint_and_radial_geometry";
            }
            try { GearRules.Validate(result.Parameters); }
            catch(ArgumentException ex) { throw new InvalidOperationException("模型齿轮参数存在歧义或数值无效，请使用 GearParameters 明确覆盖："+ex.Message); }
            return result;
        }
        private static RecognizedGeometry CentralBore(RecognitionReport recognition)
        {
            var b=recognition.BoundsMm;
            if(b==null) return null;
            return recognition.Geometry.Where(g=>g.Kind=="hole" && g.Axis!=null && g.Center!=null).FirstOrDefault(g=>Enumerable.Range(0,3).Where(i=>Math.Abs(g.Axis[i])<.1).All(i=>Math.Abs(g.Center[i]*1000-(b[i]+b[i+3])/2)<(b[i+3]-b[i])*.05));
        }
        internal static string Primary(RecognitionReport recognition)
        {
            var bore=CentralBore(recognition);
            if(bore!=null) return new[]{"Right","Top","Front"}[Enumerable.Range(0,3).OrderByDescending(i=>Math.Abs(bore.Axis[i])).First()];
            var b=recognition.BoundsMm; var axis=Enumerable.Range(0,3).OrderBy(i=>b[i+3]-b[i]).First();
            return new[]{"Right","Top","Front"}[axis];
        }
        internal static Box Reserve(Box cell,GearEvidence evidence)
        { return new Box(cell.X+cell.W*.70,cell.Y,cell.W*.30,cell.H); }
        internal static void Dimensions(ModelDoc2 doc,List<SwView> views,JobReport report)
        {
            var model=views[0].ReferencedDocument; if(model.GetType()!=1) return;
            var bodies=((PartDoc)model).GetBodies2(0,false) as object[] ?? new object[0]; if(bodies.Length!=1) { report.Warnings.Add("多实体齿轮保留原生视图，尺寸需逐体补充。"); return; }
            var body=(Body2)bodies[0]; var vertices=(body.GetVertices() as object[] ?? new object[0]).Cast<Vertex>().ToList();
            var drawing=(DrawingDoc)doc;
            // One overall profile span, one measured thickness, and the real bore. Never auto-dimension every tooth vertex.
            foreach(var view in views.Take(2)) {
                var t=(double[])view.ModelToViewTransform.ArrayData;
                Func<Vertex,double[]> transform=v=> { var p=(double[])v.GetPoint(); return new[]{p[0]*t[0]+p[1]*t[3]+p[2]*t[6],p[0]*t[1]+p[1]*t[4]+p[2]*t[7]}; };
                var mb=report.Recognition.BoundsMm; int thicknessAxis=Enumerable.Range(0,3).OrderBy(i=>mb[i+3]-mb[i]).First();
                int direction=view==views[0]?0:Math.Abs(t[thicknessAxis*3])>Math.Abs(t[thicknessAxis*3+1])?0:1;
                // Select two true vertices spanning a paper axis; read the resulting native value back.
                var sorted=vertices.OrderBy(v=>transform(v)[direction]).ToList(); if(sorted.Count<2) continue;
                var a=sorted.First(); var b=sorted.Last(); double measured=(transform(b)[direction]-transform(a)[direction])*t[12];
                if(measured<1e-7) continue;
                var box=(double[])view.GetOutline(); doc.ClearSelection2(true); drawing.ActivateView(view.GetName2());
                if(view.SelectEntity(a,false) && view.SelectEntity(b,true)) {
                    var d=(direction==0?doc.AddHorizontalDimension2((box[0]+box[2])/2,box[1]-.008,0):doc.AddVerticalDimension2(box[0]-.008,(box[1]+box[3])/2,0)) as DisplayDimension;
                    if(!doc.ForceRebuild3(false) || d==null || ((Annotation)d.GetAnnotation()).IsDangling() || Math.Abs(d.GetDimension2(0).SystemValue-measured/t[12])>1e-7) throw new InvalidOperationException("齿轮外形/厚度尺寸创建或实测值校验失败。");
                }
            }
            var main=views[0];
            foreach(Component2 component in main.GetVisibleComponents() as object[] ?? new object[0])
            foreach(Edge edge in main.GetVisibleEntities2(component,1) as object[] ?? new object[0]) {
                var curve=(Curve)edge.GetCurve(); if(!curve.IsCircle()) continue;
                var cp=(double[])curve.CircleParams;
                bool bore=report.Recognition.Geometry.Any(g=>g.Kind=="hole" && Math.Abs(g.DiameterMm-cp[6]*2000)<1e-5 && g.Entities.Contains(ModelRecognizer.PersistFromView(model,edge)));
                if(!bore) continue;
                doc.ClearSelection2(true); drawing.ActivateView(main.GetName2()); var box=(double[])main.GetOutline();
                if(main.SelectEntity(edge,false)) {
                    var d=doc.AddDiameterDimension2(box[2]+.01,box[1]-.008,0) as DisplayDimension;
                    if(d==null || Math.Abs(d.GetDimension2(0).SystemValue-cp[6]*2)>1e-7) throw new InvalidOperationException("齿轮轴孔尺寸校验失败。");
                }
                goto Done;
            }
            Done: doc.ClearSelection2(true); drawing.ActivateView("");
        }
        private static string Value(GearEvidence evidence,string key) { string value; return evidence.Parameters.TryGetValue(key,out value)?value:"未提供"; }
        internal static void InsertTable(ModelDoc2 doc,Box area,JobReport report)
        {
            var view=doc.ActiveView as ModelView; bool graphics=view==null || view.EnableGraphicsUpdate;
            try { if(view!=null) view.EnableGraphicsUpdate=false; InsertTableCore(doc,area,report); }
            finally { if(view!=null) view.EnableGraphicsUpdate=graphics; }
        }
        private static void InsertTableCore(ModelDoc2 doc,Box area,JobReport report)
        {
            var evidence=report.Gear;
            evidence.TableCells.Add(new[]{evidence.Status=="gear_parameters_required"?"齿形轮参数待补":"齿轮参数","值（来源见报告）"});
            evidence.TableCells.Add(new[]{"模数 m",Value(evidence,"Module")}); evidence.TableCells.Add(new[]{"齿数 z",Value(evidence,"Teeth")});
            evidence.TableCells.Add(new[]{"压力角 α",Value(evidence,"PressureAngle")}); evidence.TableCells.Add(new[]{"螺旋角 β",Value(evidence,"HelixAngle")});
            evidence.TableCells.Add(new[]{"齿制",Value(evidence,"GearType")}); evidence.TableCells.Add(new[]{"精度等级",Value(evidence,"Accuracy")});
            if(evidence.Calculation!=null) {
                if(evidence.Calculation.Family.StartsWith("helical")) evidence.TableCells.Add(new[]{"模数/压力角基面",evidence.Calculation.Input.ModuleSystem=="transverse"?"端面":"法向"});
                if(evidence.Calculation.Values.ContainsKey("StartAngleDeg")) {
                    evidence.TableCells.Add(new[]{"起始角 °",evidence.Calculation.Values["StartAngleDeg"].ToString("0.######",CultureInfo.InvariantCulture)});
                    evidence.TableCells.Add(new[]{"角度基准",evidence.Calculation.Input.PhaseDatum});
                    evidence.TableCells.Add(new[]{"观察/正向",evidence.Calculation.Input.ViewingDirection+" / "+evidence.Calculation.Input.PositiveRotation});
                }
            }
            foreach(string key in new[]{"ProfileShift","Hand","HeatTreatment"}) if(evidence.Parameters.ContainsKey(key)) evidence.TableCells.Add(new[]{key,evidence.Parameters[key]});
            double x=area.X+.001,y=area.Y+area.H-.001;
            var table=((DrawingDoc)doc).InsertTableAnnotation2(false,x,y,1,"",evidence.TableCells.Count,2);
            if(table==null) throw new InvalidOperationException("齿轮参数表创建失败。");
            table.TitleVisible=false;
            var format=table.GetTextFormat(); format.CharHeight=.0025; format.TypeFaceName="宋体"; table.SetTextFormat(false,format);
            for(int row=0;row<evidence.TableCells.Count;row++) {
                table.set_Text(row,0,evidence.TableCells[row][0]); table.set_Text(row,1,evidence.TableCells[row][1]);
                table.SetCellTextFormat(row,0,false,format); table.SetCellTextFormat(row,1,false,format);
                table.SetRowVerticalGap(row,.0005); table.SetRowHeight(row,.0045,0);
            }
            table.SetColumnWidth(0,area.W*.45,0); table.SetColumnWidth(1,area.W*.55,0);
            var annotation=(Annotation)table.GetAnnotation(); annotation.SetPosition2(x,y,0); evidence.TablePosition=(double[])annotation.GetPosition();
            if(Enumerable.Range(0,table.RowCount).Sum(i=>(double)table.GetRowHeight(i))>area.H-.002) throw new InvalidOperationException("齿轮参数表不能容纳在当前图幅，请使用更大图幅或减少附加参数。");
            report.Warnings.Add("齿轮采用外形/齿宽/孔径与参数表，跳过逐齿尺寸及齿形小圆角、倒角的泛化标注；齿形修形、公差、齿厚及检验要求需专业复核。");
            if(evidence.Status=="gear_parameters_required") report.Warnings.Add("当前为齿形轮候选，模数/齿数缺项：只输出真实几何和缺项参数表，请通过‘齿轮参数…’或 GearParameters 补充；未从外径推算齿制参数。");
            if(!evidence.Parameters.ContainsKey("PressureAngle") || !evidence.Parameters.ContainsKey("GearType") || !evidence.Parameters.ContainsKey("Accuracy")) report.Warnings.Add("齿轮制造参数尚不完整；表中‘未提供’保留为空缺含义，不套用默认压力角/齿制/精度。");
        }
        internal static void Verify(DrawingDoc drawing,JobReport report)
        {
            if(report.Gear==null) return;
            for(var view=drawing.GetFirstView() as SwView;view!=null;view=view.GetNextView() as SwView)
            for(var table=view.GetFirstTableAnnotation() as TableAnnotation;table!=null;table=table.GetNext() as TableAnnotation) {
                if(table.RowCount!=report.Gear.TableCells.Count || table.ColumnCount!=2) continue;
                bool match=true; for(int r=0;r<table.RowCount;r++) for(int c=0;c<2;c++) if(table.get_Text(r,c)!=report.Gear.TableCells[r][c]) match=false;
                if(match) { report.Gear.Reopened=true; return; }
            }
            throw new InvalidOperationException("齿轮参数表保存重开校验失败。");
        }
    }
}
