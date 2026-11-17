using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;

namespace AutoDrawing
{
    public sealed class DrawingOptions
    {
        public string Template { get; set; }
        public string OutputDirectory { get; set; }
        public string Configuration { get; set; }
        public bool FirstAngle { get; set; }
        public bool Isometric { get; set; }
        public bool ImportDimensions { get; set; }
        public bool AutoDimension { get; set; }
        public bool Ordinate { get; set; }
        public bool Deduplicate { get; set; }
        public bool MergeChamfers { get; set; }
        public bool DirectChamfers { get; set; }
        public bool OptimizeAnnotationLayout { get; set; }
        public bool DirectFillets { get; set; }
        public Dictionary<string,string> GearParameters { get; set; }
        public Dictionary<string,string> RackParameters { get; set; }
        public GearingInput GearInference { get; set; }
        public bool ExportPdf { get; set; }
        public bool ExportDxf { get; set; }
        public double SpacingMm { get; set; }
        public bool AutoOrient { get; set; }
        public bool AllConfigurations { get; set; }
        public bool CenterMarks { get; set; }
        public bool AutoReviewPdf { get; set; }
        public double TitleBlockHeightMm { get; set; }
        public string SheetFormat { get; set; }
        public double PaperWidthMm { get; set; }
        public double PaperHeightMm { get; set; }
        public double FixedScale { get; set; }
        public double LeftMarginMm { get; set; }
        public double RightMarginMm { get; set; }
        public double TopMarginMm { get; set; }
        public double ViewGapMm { get; set; }
        public Dictionary<string,string> DrawingProperties { get; set; }
        public DrawingOptions()
        {
            FirstAngle = true; Isometric = true; ImportDimensions = true;
            AutoDimension = true; DirectChamfers = true; OptimizeAnnotationLayout=true; DirectFillets=true;
            Deduplicate = true; ExportPdf = true; ExportDxf = true; SpacingMm = 8;
            AutoOrient=true; CenterMarks=true; AutoReviewPdf=true; TitleBlockHeightMm=65;
            LeftMarginMm=25; RightMarginMm=15; TopMarginMm=15; ViewGapMm=25;
            DrawingProperties=new Dictionary<string,string>();
            GearParameters=new Dictionary<string,string>();
            RackParameters=new Dictionary<string,string>();
        }
        public void Validate()
        {
            GearRules.Validate(GearParameters);
            RackRules.Validate(RackParameters);
            if(GearInference!=null) GearingCalculator.Validate(GearInference);
            if (!File.Exists(Template) || !string.Equals(Path.GetExtension(Template), ".drwdot", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("请选择存在的 .drwdot 工程图模板。");
            if (string.IsNullOrWhiteSpace(OutputDirectory)) throw new ArgumentException("请选择输出目录。");
            if (SpacingMm < 4 || SpacingMm > 25 || double.IsNaN(SpacingMm)) throw new ArgumentException("标注间距应为 4–25 mm。");
            if (!ImportDimensions && !AutoDimension) throw new ArgumentException("至少启用一种标注方式。");
            if (!ExportPdf) throw new ArgumentException("工程图交付需要 PDF 以供复核。");
            if (TitleBlockHeightMm<35 || TitleBlockHeightMm>150 || double.IsNaN(TitleBlockHeightMm)) throw new ArgumentException("标题栏预留应为 35–150 mm。");
            if(AllConfigurations && !string.IsNullOrWhiteSpace(Configuration)) throw new ArgumentException("全部配置模式下请清空指定配置名称。");
            if(!string.IsNullOrWhiteSpace(SheetFormat) && (!File.Exists(SheetFormat) || !Path.GetExtension(SheetFormat).Equals(".slddrt",StringComparison.OrdinalIgnoreCase))) throw new ArgumentException("图纸格式必须为存在的 .slddrt 文件。");
            if(!Finite(PaperWidthMm) || !Finite(PaperHeightMm) || !Finite(FixedScale) || PaperWidthMm<0 || PaperHeightMm<0 || FixedScale<0 || FixedScale>100) throw new ArgumentException("图幅和比例参数无效。");
            if((PaperWidthMm==0)!=(PaperHeightMm==0) || (PaperWidthMm>0 && (PaperWidthMm<180 || PaperHeightMm<150 || PaperWidthMm>5000 || PaperHeightMm>5000))) throw new ArgumentException("自定义图幅必须同时指定宽高，最小 180×150 mm，最大 5000 mm。");
            if(new[]{LeftMarginMm,RightMarginMm,TopMarginMm,ViewGapMm}.Any(x=>!Finite(x) || x<5 || x>150)) throw new ArgumentException("边距和视图间隔必须为 5–150 mm。");
            if(DrawingProperties==null || DrawingProperties.Any(p=>string.IsNullOrWhiteSpace(p.Key) || p.Value==null)) throw new ArgumentException("标题栏属性名称和值不能为空/null。");
        }
        private static bool Finite(double x) { return !double.IsNaN(x) && !double.IsInfinity(x); }
    }
    public sealed class Box
    {
        public double X, Y, W, H;
        public Box(double x, double y, double w, double h) { X=x; Y=y; W=w; H=h; }
        public bool Contains(double[] b, double padding)
        { return b.Length >= 4 && b[0]>=X+padding && b[1]>=Y+padding && b[2]<=X+W-padding && b[3]<=Y+H-padding; }
    }
    public static class LayoutPlanner
    {
        // Reserve border, dimension lanes and the bottom title block. All API units are metres.
        public static Box[] Cells(double w, double h, bool firstAngle, double titleHeight=.065)
        { return Cells(w,h,firstAngle,titleHeight,.025,.015,.015,.025); }
        public static Box[] Cells(double w,double h,bool firstAngle,double titleHeight,double left,double right,double top,double gap)
        {
            if (w < .18 || h < .15) throw new ArgumentException("模板图幅过小。");
            double bottom=titleHeight;
            if(h-bottom<.075) throw new ArgumentException("标题栏预留过大，视图区高度不足。");
            double cw=(w-left-right-gap)/2, ch=(h-bottom-top-gap)/2;
            if(cw<.035 || ch<.035) throw new ArgumentException("边距或间隔过大，视图区不足。");
            var lowerLeft=new Box(left,bottom,cw,ch);
            var upperLeft=new Box(left,bottom+ch+gap,cw,ch);
            var upperRight=new Box(left+cw+gap,bottom+ch+gap,cw,ch);
            var lowerRight=new Box(left+cw+gap,bottom,cw,ch);
            return firstAngle ? new[]{upperLeft,lowerLeft,upperRight,lowerRight} : new[]{lowerLeft,upperLeft,lowerRight,upperRight};
        }
        public static double Fit(IEnumerable<Tuple<double,double,Box>> views)
        {
            double limit=views.Min(v=>Math.Min((v.Item3.W-.012)/v.Item1,(v.Item3.H-.012)/v.Item2));
            double[] preferred={10,5,2,1,.5,.25,.2,.1,.05,.02,.01,.005,.002,.001};
            if (double.IsNaN(limit) || limit<=0) throw new InvalidOperationException("视图边界无效。");
            return preferred.FirstOrDefault(s=>s<=limit) > 0 ? preferred.First(s=>s<=limit) : limit*.9;
        }
    }
    public sealed class DimensionEvidence
    {
        public string View { get; set; }
        public string Model { get; set; }
        public string Configuration { get; set; }
        public string FullName { get; set; }
        public string Semantic { get; set; }
        public string Identity { get; set; }
        public string GeometryKey { get; set; }
        public double ValueSI { get; set; }
        public double ChamferAngleDegrees { get; set; }
        public int ChamferTextStyle { get; set; }
        public int Type { get; set; }
        public bool Dangling { get; set; }
        public bool ValueVerified { get; set; }
        public string DisplayText { get; set; }
        public DimensionEvidence() { ValueVerified=true; }
        public double[] Position { get; set; }
        public List<string> Entities { get; set; }
    }
    public static class DimensionRules
    {
        // Equal numeric values are NEVER a key. Require the same model parameter and presentation semantics.
        public static string Key(DimensionEvidence d)
        {
            if (d.Dangling || !d.ValueVerified || string.IsNullOrWhiteSpace(d.FullName) || string.IsNullOrWhiteSpace(d.Identity)) return null;
            return string.Join("\u001f",new[]{d.Model,d.Configuration,string.IsNullOrWhiteSpace(d.GeometryKey)?d.FullName:d.GeometryKey,d.Semantic,d.Identity});
        }
    }
    public sealed class JobReport
    {
        public string GeneratorVersion { get; set; }
        public string GeneratorAssemblyPath { get; set; }
        public string Model { get; set; }
        public string Drawing { get; set; }
        public string Pdf { get; set; }
        public string Dxf { get; set; }
        public string Status { get; set; }
        public string Error { get; set; }
        public int RemovedDuplicates { get; set; }
        public List<RemovedRedundantDimension> RemovedRedundantDimensions { get; set; }
        public int MergedChamferGroups { get; set; }
        public List<ChamferCalloutEvidence> ChamferCallouts { get; set; }
        public List<RemovedChamferDimension> RemovedChamferDimensions { get; set; }
        public List<SkippedChamfer> SkippedChamfers { get; set; }
        public int ChamferSilhouetteScans { get; set; }
        public AnnotationLayoutEvidence AnnotationLayout { get; set; }
        public List<RadiusCalloutEvidence> RadiusCallouts { get; set; }
        public GearEvidence Gear { get; set; }
        public RackEvidence Rack { get; set; }
        public GearingResult GearingAnalysis { get; set; }
        public List<string> Warnings { get; set; }
        public List<DimensionEvidence> Dimensions { get; set; }
        public string Configuration { get; set; }
        public RecognitionReport Recognition { get; set; }
        public List<StageEvidence> Stages { get; set; }
        public List<AutoDimensionEvidence> AutoDimensions { get; set; }
        public int CenterMarkCount { get; set; }
        public int RepositionedDimensions { get; set; }
        public string PdfReview { get; set; }
        public string Revision { get; set; }
        public DrawingOptions Options { get; set; }
        public Dictionary<string,string> DrawingProperties { get; set; }
        public double[] SheetSizeMm { get; set; }
        public string SheetFormat { get; set; }
        public SheetInitializationEvidence SheetInitialization { get; set; }
        public List<ThreadCalloutEvidence> ThreadCallouts { get; set; }
        public JobReport() { GeneratorVersion=RuntimeInfo.Version; GeneratorAssemblyPath=RuntimeInfo.AssemblyPath; Status="failed"; RemovedRedundantDimensions=new List<RemovedRedundantDimension>(); Warnings=new List<string>(); Dimensions=new List<DimensionEvidence>(); Stages=new List<StageEvidence>(); AutoDimensions=new List<AutoDimensionEvidence>(); ThreadCallouts=new List<ThreadCalloutEvidence>(); ChamferCallouts=new List<ChamferCalloutEvidence>(); RemovedChamferDimensions=new List<RemovedChamferDimension>(); SkippedChamfers=new List<SkippedChamfer>(); RadiusCallouts=new List<RadiusCalloutEvidence>(); }
        public void Write(string path) { File.WriteAllText(path,new JavaScriptSerializer { MaxJsonLength=int.MaxValue }.Serialize(this),System.Text.Encoding.UTF8); }
    }
    public sealed class StageEvidence { public string Name { get; set; } public long ElapsedMs { get; set; } }
    public sealed class AutoDimensionEvidence { public string View { get; set; } public int ReturnCode { get; set; } public int Before { get; set; } public int After { get; set; } public int Attempts { get; set; } }
}
