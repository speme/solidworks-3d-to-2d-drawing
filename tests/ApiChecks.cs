using System;
using System.Collections.Generic;
using System.IO;
using AutoDrawing;
class ApiChecks
{
    static int count;
    static void Check(bool ok,string message) { count++; if(!ok) throw new Exception(message); }
    static bool Reject(Action action) { try { action(); return false; } catch(ArgumentException) { return true; } }
    static int Main()
    {
        try { Run(); return 0; }
        catch(Exception ex) { Console.WriteLine(ex.GetType().FullName); Console.WriteLine(ex.Message); Console.WriteLine(ex.StackTrace); return 1; }
    }
    static void Run()
    {
        string root=Path.Combine(Path.GetTempPath(),"AutoDrawing-api-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
        string template=Path.Combine(root,"company.drwdot"),model=Path.Combine(root,"part.sldprt"); File.WriteAllText(template,"test"); File.WriteAllText(model,"test");
        var profile=new TemplateProfile { Name="Company",Options=new Dictionary<string,object> { {"Template","company.drwdot"},{"SpacingMm",10},{"DrawingProperties",new Dictionary<string,object>{{"Company","ABC"},{"DrawingNo","OLD"}}} } };
        var json=new System.Web.Script.Serialization.JavaScriptSerializer(); string profilePath=Path.Combine(root,"company.json"); File.WriteAllText(profilePath,json.Serialize(profile));
        var req=new GenerationRequest { TemplateProfile="company.json",Models=new List<GenerationInput>{new GenerationInput{Path="part.sldprt"},new GenerationInput{Path="part.sldprt"}},Options=new Dictionary<string,object> { {"OutputDirectory","output"},{"SpacingMm",6},{"DrawingProperties",new Dictionary<string,object>{{"DrawingNo","NEW"}}} } };
        var r=GenerationService.Resolve(req,root);
        Check(r.Options.ExportDxf,"未指定时应与 PDF 同时默认导出 DXF");
        Check(r.Models.Count==1,"同一模型和配置去重");
        Check(r.Options.Template==template && r.Options.OutputDirectory==Path.Combine(root,"output"),"请求和模板的相对路径解析");
        Check(r.Options.SpacingMm==6 && r.Options.DrawingProperties["Company"]=="ABC" && r.Options.DrawingProperties["DrawingNo"]=="NEW","覆盖顺序与字典深合并");
        profile.Options["RackParameters"]=new Dictionary<string,object>{{"Module","5"}};
        File.WriteAllText(profilePath,json.Serialize(profile));
        req.Options["RackParameters"]=new Dictionary<string,object>{{"PressureAngle","20"}};
        var rackOptions=GenerationService.Resolve(req,root).Options;
        Check(rackOptions.RackParameters["Module"]=="5" && rackOptions.RackParameters["PressureAngle"]=="20","齿条模板参数与请求参数按键合并");
        req.Options["RackParameters"]=new Dictionary<string,object>{{"PressureAngle","90"}};
        Check(Reject(()=>GenerationService.Resolve(req,root)),"拒绝无效齿条压力角");
        req.Options.Remove("RackParameters");
        req.Options["GearInference"]=new Dictionary<string,object>{{"Family","spur_external"},{"Measurements",new Dictionary<string,object>{{"PitchDiameterMm",48},{"Teeth",24}}}};
        Check(GenerationService.Resolve(req,root).Options.GearInference.Measurements["Teeth"]==24,"原图公式输入进入同一请求与模板解析流程");
        req.Options["GearInference"]=new Dictionary<string,object>{{"Family","spur_external"},{"Measurments",new Dictionary<string,object>()}};
        Check(Reject(()=>GenerationService.Resolve(req,root)),"拒绝公式嵌套字段拼写错误"); req.Options.Remove("GearInference");
        req.Models.Add(new GenerationInput { Path="part.sldprt",Configuration="Thin" });
        Check(GenerationService.Resolve(req,root).Models.Count==2,"同文件不同配置不得丢失");
        req.Options["Typo"]=true; Check(Reject(()=>GenerationService.Resolve(req,root)),"未知选项必须拒绝"); req.Options.Remove("Typo");
        req.SchemaVersion="2.0"; Check(Reject(()=>GenerationService.Resolve(req,root)),"版本不支持必须拒绝"); req.SchemaVersion="1.0";
        req.Options["AllConfigurations"]=true; Check(Reject(()=>GenerationService.Resolve(req,root)),"全部配置与单件配置冲突"); req.Options.Remove("AllConfigurations");
        var options=r.Options; options.PaperWidthMm=420; Check(Reject(options.Validate),"图幅必须成对填写"); options.PaperHeightMm=297; options.Validate();
        options.ViewGapMm=double.PositiveInfinity; Check(Reject(options.Validate),"布局参数拒绝非有限数"); options.ViewGapMm=25;
        string saved=Path.Combine(root,"saved.json"); TemplateProfiles.Save(saved,"Saved",options);
        var loaded=TemplateProfiles.Load(saved);
        Check(!loaded.Options.ContainsKey("OutputDirectory") && !loaded.Options.ContainsKey("Configuration"),"模板配置不固化任务路径和模型配置");
        var cells=LayoutPlanner.Cells(.42,.297,true,.065,.03,.02,.02,.03);
        Check(cells[0].X==.03 && Math.Abs(cells[0].W-.17)<1e-9,"自定义边距参与布局");
        string requestPath=Path.Combine(root,"bad.json"); File.WriteAllText(requestPath,"{\"SchemaVersion\":\"1.0\",\"Models\":[{\"Path\":\"part.sldprt\",\"Typo\":1}]}");
        Check(Reject(()=>GenerationRequest.Load(requestPath)),"模型项的拼写错误必须拒绝");
        var result=new GenerationResult { Status="validated" }; string resultPath=Path.Combine(root,"result.json"); result.Write(resultPath);
        bool protectedFile=false; try { result.Write(resultPath); } catch(IOException) { protectedFile=true; }
        Check(protectedFile,"调用结果不能覆盖已有文件");
        Console.WriteLine("PASS: "+count+" API/profile checks");
    }
}
