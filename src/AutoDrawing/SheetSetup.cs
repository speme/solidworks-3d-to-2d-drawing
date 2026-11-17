using System;
using System.IO;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace AutoDrawing
{
    public sealed class SheetInitializationEvidence
    {
        public string Mode { get; set; }
        public string OriginalFormatPath { get; set; }
        public bool OriginalFormatFileExists { get; set; }
        public double[] Before { get; set; }
        public double[] After { get; set; }
        public int EmbeddedSegmentsBefore { get; set; }
        public int EmbeddedSegmentsAfter { get; set; }
        public int EmbeddedSegmentsReopened { get; set; }
    }
    internal static class SheetSetup
    {
        internal static int Segments(Sheet sheet)
        {
            var sketch=sheet.GetTemplateSketch() as Sketch;
            if(sketch==null) return 0;
            var segments=sketch.GetSketchSegments() as object[];
            return segments==null?0:segments.Length;
        }
        internal static void Apply(ModelDoc2 model,DrawingOptions options,JobReport report)
        {
            var drawing=(DrawingDoc)model; var sheet=(Sheet)drawing.GetCurrentSheet();
            double width=0,height=0; sheet.GetSize(ref width,ref height);
            var properties=(double[])sheet.GetProperties2();
            string original=sheet.GetTemplateName();
            var evidence=new SheetInitializationEvidence {
                Mode=string.IsNullOrWhiteSpace(options.SheetFormat)?"preserve_embedded":"replace_format",
                OriginalFormatPath=original,OriginalFormatFileExists=File.Exists(original),Before=properties,
                EmbeddedSegmentsBefore=Segments(sheet),EmbeddedSegmentsReopened=-1
            };
            report.SheetInitialization=evidence;
            int paper=(int)properties[0],format=(int)properties[1];
            if(options.PaperWidthMm>0) { paper=(int)swDwgPaperSizes_e.swDwgPapersUserDefined; width=options.PaperWidthMm/1000; height=options.PaperHeightMm/1000; }
            if(evidence.Mode=="preserve_embedded")
            {
                // Templates may contain a valid embedded frame but a retired .slddrt source path.
                // SetProperties2 updates paper/projection/scale without reloading that source file.
                sheet.SetProperties2(paper,format,1,1,options.FirstAngle,width,height,properties[7]!=0);
                if(!string.IsNullOrWhiteSpace(original) && !evidence.OriginalFormatFileExists)
                    report.Warnings.Add("模板记录的图框源文件不可访问，已保留内嵌图框："+original);
            }
            else
            {
                format=(int)swDwgTemplates_e.swDwgTemplateCustom;
                string requested=Path.GetFullPath(options.SheetFormat);
                if(!drawing.SetupSheet5(sheet.GetName(),paper,format,1,1,options.FirstAngle,requested,width,height,"",false))
                    throw new InvalidOperationException(string.Format("替换图框失败：模板={0}，图纸={1}，Paper={2}，Format={3}，Size={4}x{5} mm，FirstAngle={6}，SheetFormat={7}。",options.Template,sheet.GetName(),paper,format,width*1000,height*1000,options.FirstAngle,requested));
            }
            if(!model.ForceRebuild3(false)) throw new InvalidOperationException("图纸属性更新后重建失败。");
            evidence.After=(double[])sheet.GetProperties2(); evidence.EmbeddedSegmentsAfter=Segments(sheet);
            if((evidence.After[4]!=0)!=options.FirstAngle) throw new InvalidOperationException("投影法回读不一致："+options.Template);
            double actualWidth=0,actualHeight=0; sheet.GetSize(ref actualWidth,ref actualHeight);
            if(Math.Abs(actualWidth-width)>1e-8 || Math.Abs(actualHeight-height)>1e-8) throw new InvalidOperationException("图幅回读不一致："+options.Template);
            string actualFormat=sheet.GetTemplateName();
            if(evidence.Mode=="preserve_embedded")
            {
                if(!string.Equals(actualFormat,original,StringComparison.OrdinalIgnoreCase) || evidence.EmbeddedSegmentsAfter!=evidence.EmbeddedSegmentsBefore)
                    throw new InvalidOperationException("更新图纸属性后内嵌图框发生变化，已停止出图。");
            }
            else if(!string.Equals(Path.GetFullPath(actualFormat),Path.GetFullPath(options.SheetFormat),StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("图框文件路径回读不一致。");
            report.SheetSizeMm=new[]{actualWidth*1000,actualHeight*1000}; report.SheetFormat=actualFormat;
        }
    }
}
