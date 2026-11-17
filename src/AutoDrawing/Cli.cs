using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using AutoDrawing;
using SolidWorks.Interop.sldworks;

internal static class Cli
{
    private static volatile bool cancelled;
    [STAThread]
    private static int Main(string[] args)
    {
        Console.OutputEncoding=new UTF8Encoding(false);
        string resultPath=null; bool generating=false;
        var result=new GenerationResult();
        try
        {
            if(args.Length==1 && args[0]=="--version") { Console.WriteLine(RuntimeInfo.ToJson()); return 0; }
            if(args.Length>0 && args[0]=="calculate-gearing") { var calculationArgs=new string[args.Length-1]; Array.Copy(args,1,calculationArgs,0,calculationArgs.Length); return GearingCalculator.RunCli(calculationArgs); }
            if(args.Length==0 || args[0]=="--help" || args[0]=="help") { Help(); return 0; }
            string command=args[0]; if(command!="generate" && command!="validate") throw new ArgumentException("命令必须为 generate 或 validate。");
            string requestPath=null; var overrides=new Dictionary<string,object>(); var inputs=new List<GenerationInput>(); string profile=null; bool connectOnly=false;
            for(int i=1;i<args.Length;i++)
            {
                string flag=args[i];
                if(flag=="--all-configurations") { overrides["AllConfigurations"]=true; continue; }
                if(flag=="--connect-only") { connectOnly=true; continue; }
                if(i+1>=args.Length) throw new ArgumentException("参数缺少值："+flag);
                string value=args[++i];
                switch(flag)
                {
                    case "--request": requestPath=Path.GetFullPath(value); break;
                    case "--result": resultPath=Path.GetFullPath(value); break;
                    case "--model": inputs.Add(new GenerationInput { Path=value }); break;
                    case "--template": overrides["Template"]=value; break;
                    case "--profile": profile=value; break;
                    case "--output": overrides["OutputDirectory"]=value; break;
                    case "--configuration": overrides["Configuration"]=value; break;
                    default: throw new ArgumentException("未知命令行参数："+flag);
                }
            }
            if(resultPath!=null && File.Exists(resultPath)) { string occupied=resultPath; resultPath=null; throw new ArgumentException("结果文件已存在，请使用新文件名："+occupied); }
            if(requestPath!=null && (inputs.Count>0 || overrides.Count>0 || profile!=null)) throw new ArgumentException("--request 不能与模型/模板/配置参数混用；仅可搭配 --result 和 --connect-only。");
            string root=requestPath==null?Directory.GetCurrentDirectory():Path.GetDirectoryName(requestPath);
            var request=requestPath==null?new GenerationRequest { Models=inputs,Options=overrides,TemplateProfile=profile }:GenerationRequest.Load(requestPath);
            if(connectOnly) request.StartSolidWorks=false;
            var resolved=GenerationService.Resolve(request,root); result.RequestId=resolved.RequestId;
            if(command=="validate") { result.Resolved=resolved; result.Status="validated"; result.ExitCode=0; }
            else
            {
                if(resultPath!=null) { Directory.CreateDirectory(Path.GetDirectoryName(resultPath)); using(var test=new FileStream(resultPath+".probe-"+Guid.NewGuid().ToString("N"),FileMode.CreateNew,FileAccess.Write,FileShare.None,1,FileOptions.DeleteOnClose)) {} }
                Console.CancelKeyPress+=(s,e)=> { e.Cancel=true; cancelled=true; };
                SldWorks app;
                try
                {
                    try { app=(SldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
                    catch(COMException) { if(!request.StartSolidWorks) throw; app=(SldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application",true)); app.Visible=true; }
                }
                catch(Exception ex) { result.ExitCode=3; result.Status="unavailable"; result.ErrorCode="SOLIDWORKS_UNAVAILABLE"; result.Error=ex.Message; return Emit(result,resultPath); }
                generating=true;
                result=GenerationService.Generate(app,request,root,s=>Console.Error.WriteLine(s),()=>cancelled);
            }
        }
        catch(Exception ex) { result.ExitCode=generating?4:2; result.Status=generating?"failed":"invalid_request"; result.ErrorCode=generating?"GENERATION_ERROR":"INVALID_REQUEST"; result.Error=ex.Message; }
        return Emit(result,resultPath);
    }
    private static int Emit(GenerationResult result,string path)
    {
        result.Finished=DateTimeOffset.Now.ToString("o");
        if(path!=null) try { result.Write(path); } catch(Exception ex) { result.ExitCode=6; result.Status="result_write_failed"; result.ErrorCode="RESULT_WRITE_FAILED"; result.Error=ex.Message; }
        Console.Out.WriteLine(result.ToJson()); return result.ExitCode;
    }
    private static void Help()
    {
        Console.WriteLine("AutoDrawing "+RuntimeInfo.Version+" CLI (Windows x64 / interactive desktop / SolidWorks)");
        Console.WriteLine("  AutoDrawing.Cli.exe --version");
        Console.WriteLine("  AutoDrawing.Cli.exe generate --request job.json [--result result.json] [--connect-only]");
        Console.WriteLine("  AutoDrawing.Cli.exe validate --request job.json [--result result.json]");
        Console.WriteLine("  AutoDrawing.Cli.exe generate --model part.SLDPRT --template A3.drwdot --output drawings");
        Console.WriteLine("  Optional: --profile company.json --configuration Default --all-configurations");
        Console.WriteLine("calculate-gearing --request measurements.json [--result calculation.json] (no SolidWorks connection; angles in degrees, lengths in mm)");
        Console.WriteLine("stdout: one JSON result; stderr: progress. Exit 0=generated/review required, 2=invalid, 3=SW unavailable, 4=job failed/conflict, 5=busy, 6=result write failed, 130=cancelled.");
    }
}
