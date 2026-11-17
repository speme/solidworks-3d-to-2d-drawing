using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using SolidWorks.Interop.sldworks;

namespace AutoDrawing
{
    public sealed class GenerationInput
    { public string Path { get; set; } public string Configuration { get; set; } }
    public sealed class GenerationRequest
    {
        public string SchemaVersion { get; set; }
        public string RequestId { get; set; }
        public List<GenerationInput> Models { get; set; }
        public string TemplateProfile { get; set; }
        public Dictionary<string,object> Options { get; set; }
        public bool StartSolidWorks { get; set; }
        public bool StopOnError { get; set; }
        public GenerationRequest() { SchemaVersion="1.0"; Models=new List<GenerationInput>(); Options=new Dictionary<string,object>(); StartSolidWorks=true; }
        public static GenerationRequest Load(string path)
        {
            var json=TemplateProfiles.Json(); var raw=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(path));
            if(raw==null) throw new ArgumentException("请求为空。");
            TemplateProfiles.KnownKeys(raw,typeof(GenerationRequest).GetProperties().Select(p=>p.Name),"请求");
            object models;
            if(raw.TryGetValue("Models",out models) && models is System.Collections.IEnumerable)
                foreach(var item in (System.Collections.IEnumerable)models) {
                    var data=item as Dictionary<string,object>; if(data==null) throw new ArgumentException("Models 元素必须为对象。");
                    TemplateProfiles.KnownKeys(data,new[]{"Path","Configuration"},"Models");
                }
            return json.ConvertToType<GenerationRequest>(raw);
        }
    }
    public sealed class ResolvedGenerationRequest
    {
        public string RequestId { get; set; }
        public DrawingOptions Options { get; set; }
        public List<GenerationInput> Models { get; set; }
    }
    public sealed class GenerationResult
    {
        public string SchemaVersion { get; set; }
        public string RequestId { get; set; }
        public string Status { get; set; }
        public int ExitCode { get; set; }
        public string ErrorCode { get; set; }
        public string Error { get; set; }
        public string Started { get; set; }
        public string Finished { get; set; }
        public int Planned { get; set; }
        public int Completed { get; set; }
        public int Failed { get; set; }
        public List<JobReport> Jobs { get; set; }
        public ResolvedGenerationRequest Resolved { get; set; }
        public GenerationResult() { SchemaVersion="1.0"; Started=DateTimeOffset.Now.ToString("o"); Jobs=new List<JobReport>(); }
        public string ToJson() { return new System.Web.Script.Serialization.JavaScriptSerializer { MaxJsonLength=int.MaxValue }.Serialize(this); }
        public void Write(string path)
        {
            path=Path.GetFullPath(path); Directory.CreateDirectory(Path.GetDirectoryName(path));
            // Refuse an existing result, just as the drawing engine refuses to overwrite existing outputs.
            using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))
            using(var writer=new StreamWriter(stream,new System.Text.UTF8Encoding(false))) writer.Write(ToJson());
        }
    }
    public static class GenerationService
    {
        public static ResolvedGenerationRequest Resolve(GenerationRequest request,string baseDirectory)
        {
            if(request==null || request.SchemaVersion!="1.0") throw new ArgumentException("请求 SchemaVersion 必须为 1.0。");
            string root=Path.GetFullPath(baseDirectory??Directory.GetCurrentDirectory());
            var options=TemplateProfiles.Resolve(request.TemplateProfile,request.Options,root);
            if(request.Models==null || request.Models.Count==0) throw new ArgumentException("至少指定一个 Models 输入。");
            var models=new List<GenerationInput>(); var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach(var input in request.Models)
            {
                if(input==null || string.IsNullOrWhiteSpace(input.Path)) throw new ArgumentException("Models.Path 不能为空。");
                string path=TemplateProfiles.ResolvePath(input.Path,root);
                if(!File.Exists(path) || !BatchPlanner.IsModel(path)) throw new ArgumentException("模型不存在或格式不支持："+path);
                if(options.AllConfigurations && !string.IsNullOrWhiteSpace(input.Configuration)) throw new ArgumentException("全部配置与单件指定配置不能同时使用。");
                if(seen.Add(path+"\u001f"+(input.Configuration??options.Configuration??""))) models.Add(new GenerationInput { Path=path,Configuration=input.Configuration??options.Configuration });
            }
            return new ResolvedGenerationRequest { RequestId=string.IsNullOrWhiteSpace(request.RequestId)?Guid.NewGuid().ToString("N"):request.RequestId,Options=options,Models=models };
        }
        public static GenerationResult Generate(SldWorks app,GenerationRequest request,string baseDirectory=null,Action<string> progress=null,Func<bool> cancelled=null)
        {
            if(app==null) throw new ArgumentNullException("app");
            if(Thread.CurrentThread.GetApartmentState()!=ApartmentState.STA) throw new InvalidOperationException("GenerationService 必须在 STA 线程调用。");
            var resolved=Resolve(request,baseDirectory);
            var result=new GenerationResult { RequestId=resolved.RequestId,Resolved=resolved };
            using(var gate=new Mutex(false,"Local\\AutoDrawing.SolidWorks."+app.GetProcessID()))
            {
                bool held; try { held=gate.WaitOne(0); } catch(AbandonedMutexException) { held=true; }
                if(!held) { result.ExitCode=5; result.Status="busy"; result.ErrorCode="SESSION_BUSY"; result.Error="此 SolidWorks 实例正在出图。"; result.Finished=DateTimeOffset.Now.ToString("o"); return result; }
                try
                {
                    var jobs=new List<BatchJob>();
                    foreach(var input in resolved.Models)
                    {
                        var perModel=BatchPlanner.ForJob(resolved.Options,new BatchJob { Configuration=input.Configuration });
                        perModel.AllConfigurations=resolved.Options.AllConfigurations;
                        jobs.AddRange(BatchPlanner.Expand(app,new[]{input.Path},perModel));
                    }
                    result.Planned=jobs.Count; var engine=new DrawingEngine(app);
                    foreach(var job in jobs)
                    {
                        if(cancelled!=null && cancelled()) { result.Status="cancelled"; result.ExitCode=130; break; }
                        JobReport report;
                        try
                        {
                            if(job.PreflightError!=null) throw new InvalidOperationException(job.PreflightError);
                            report=engine.Run(job.Model,BatchPlanner.ForJob(resolved.Options,job),progress);
                        }
                        catch(Exception ex) { report=new JobReport { Model=job.Model,Configuration=job.Configuration,Error=ex.ToString() }; }
                        result.Jobs.Add(report);
                        if(report.Status=="failed") { result.Failed++; if(request.StopOnError) break; } else result.Completed++;
                    }
                    if(result.Status!="cancelled") { result.ExitCode=result.Failed>0?4:0; result.Status=result.Failed>0?"failed":"generated_review_required"; }
                    if(result.Failed>0) result.ErrorCode="JOB_FAILED";
                }
                finally { gate.ReleaseMutex(); result.Finished=DateTimeOffset.Now.ToString("o"); }
            }
            return result;
        }
    }
}
