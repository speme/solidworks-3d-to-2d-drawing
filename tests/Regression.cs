using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using AutoDrawing;
using SolidWorks.Interop.sldworks;
class Regression
{
    static void Check(bool value,string text) { if(!value) throw new Exception(text); }
    [STAThread]
    static int Main(string[] args)
    {
        SldWorks app;
        try { app=(SldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
        catch(COMException) { app=(SldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application")); app.Visible=true; }
        string root=Path.GetFullPath(args[0]);
        var summary=new BatchSummary();
        var plan=BatchPlanner.Expand(app,Directory.GetFiles(root,"*.SLDPRT").OrderBy(x=>x),new DrawingOptions { AllConfigurations=true });
        foreach(var job in plan)
        {
            string path=job.Model;
            string name=Path.GetFileNameWithoutExtension(path); Console.WriteLine("TEST: "+name);
            var options=new DrawingOptions { Template=args[1],OutputDirectory=Path.Combine(root,"drawings"),AutoReviewPdf=true,Configuration=job.Configuration };
            var report=new DrawingEngine(app).Run(path,options,Console.WriteLine); summary.Jobs.Add(report);
            if(report.Error!=null) { Console.WriteLine(report.Error); continue; }
            try
            {
                Check(report.Status=="review_required","生成失败");
                var r=report.Recognition;
                Check(r!=null && report.Dimensions.Count>0,"没有识别结果或尺寸");
                Check(report.PdfReview!=null,"PDF 审查未执行");
                if(name=="shaft") Check(r.PartClass=="shaft" && r.Geometry.Count(g=>g.Kind=="outer_cylinder")==1,"轴件识别错误");
                if(name=="top-holes") Check(r.PrimaryView=="Top" && r.Geometry.Count(g=>g.Kind=="hole" && g.Extent=="through")==2 && report.CenterMarkCount>0,"俯视孔件识别/方向错误");
                if(name=="slot") Check(r.Geometry.Count(g=>g.Kind=="slot" && Math.Abs(g.DiameterMm-10)<.001 && Math.Abs(g.LengthMm-50)<.001)==1,"腰槽识别错误");
                if(name=="blind-hole") Check(r.Geometry.Count(g=>g.Kind=="hole" && g.Extent=="blind" && Math.Abs(g.DepthMm-12)<.001)==1,"盲孔识别错误");
                if(name=="stepped-bore") Check(r.Geometry.Count(g=>g.Kind=="hole")==2 && r.Notes.Any(n=>n.Contains("阶梯孔")),"阶梯孔识别错误");
                if(name=="two-thickness") Check(Math.Abs(r.BoundsMm[5]-r.BoundsMm[2]-(job.Configuration=="Thick"?20:10))<.001,"配置几何或活动配置隔离失败");
                Console.WriteLine("PASS "+name+" | "+report.Dimensions.Count+" dims | "+report.CenterMarkCount+" marks | "+r.PrimaryView);
            }
            catch(Exception ex) { report.Status="failed"; report.Error=ex.ToString(); Console.WriteLine("FAIL "+ex.Message); }
        }
        summary.Planned=summary.Jobs.Count; string manifest=summary.Write(Path.Combine(root,"drawings")); Console.WriteLine(manifest);
        return summary.Jobs.Any(j=>j.Status=="failed")?1:0;
    }
}
