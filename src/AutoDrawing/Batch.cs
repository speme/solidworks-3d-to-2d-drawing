using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using SolidWorks.Interop.sldworks;

namespace AutoDrawing
{
    public sealed class BatchJob
    {
        public string Model { get; set; }
        public string Configuration { get; set; }
        public string PreflightError { get; set; }
        public override string ToString() { return Path.GetFileName(Model)+(string.IsNullOrEmpty(Configuration)?"":" ["+Configuration+"]"); }
    }
    public sealed class BatchSummary
    {
        public string GeneratorVersion { get; set; }
        public string GeneratorAssemblyPath { get; set; }
        public string Started { get; set; }
        public string Finished { get; set; }
        public int Planned { get; set; }
        public int Failed { get; set; }
        public bool Cancelled { get; set; }
        public List<JobReport> Jobs { get; set; }
        public BatchSummary() { GeneratorVersion=RuntimeInfo.Version; GeneratorAssemblyPath=RuntimeInfo.AssemblyPath; Started=DateTimeOffset.Now.ToString("o"); Jobs=new List<JobReport>(); }
        public string Write(string directory)
        {
            Finished=DateTimeOffset.Now.ToString("o"); Failed=Jobs.Count(j=>j.Status=="failed");
            Directory.CreateDirectory(directory);
            string path=Path.Combine(directory,"batch_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")+".json");
            File.WriteAllText(path,new JavaScriptSerializer { MaxJsonLength=int.MaxValue }.Serialize(this),System.Text.Encoding.UTF8); return path;
        }
    }
    public static class BatchPlanner
    {
        public static bool IsModel(string path)
        { string ext=Path.GetExtension(path); return !Path.GetFileName(path).StartsWith("~$") && (ext.Equals(".sldprt",StringComparison.OrdinalIgnoreCase)||ext.Equals(".sldasm",StringComparison.OrdinalIgnoreCase)); }
        public static IEnumerable<string> Discover(string directory)
        {
            var pending=new Stack<string>(); pending.Push(Path.GetFullPath(directory));
            while(pending.Count>0)
            {
                string path=pending.Pop(); string[] files,sub;
                try { files=Directory.GetFiles(path); sub=Directory.GetDirectories(path); }
                catch(UnauthorizedAccessException) { continue; }
                catch(IOException) { continue; }
                foreach(string file in files.Where(IsModel)) yield return file;
                foreach(string child in sub)
                {
                    try { if((File.GetAttributes(child)&FileAttributes.ReparsePoint)==0) pending.Push(child); }
                    catch(UnauthorizedAccessException) { }
                    catch(IOException) { }
                }
            }
        }
        public static List<BatchJob> Expand(SldWorks app,IEnumerable<string> paths,DrawingOptions options)
        {
            var jobs=new List<BatchJob>();
            var previous=app.ActiveDoc as ModelDoc2;
            foreach(string path in paths.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if(!options.AllConfigurations) { jobs.Add(new BatchJob { Model=path,Configuration=options.Configuration }); continue; }
                var doc=app.GetOpenDocumentByName(path) as ModelDoc2; bool opened=doc==null;
                try
                {
                    int error=0,warning=0;
                    if(opened) doc=app.OpenDoc6(path,Path.GetExtension(path).Equals(".sldasm",StringComparison.OrdinalIgnoreCase)?2:1,3,"",ref error,ref warning);
                    if(doc==null || error!=0) throw new IOException("读取配置失败："+path+" 错误 "+error);
                    foreach(string config in ((string[])doc.GetConfigurationNames()).OrderBy(x=>x,StringComparer.Ordinal))
                        jobs.Add(new BatchJob { Model=path,Configuration=config });
                }
                catch(Exception ex) { jobs.Add(new BatchJob { Model=path,PreflightError=ex.Message }); }
                finally { if(opened && doc!=null) app.CloseDoc(doc.GetTitle()); }
            }
            if(previous!=null) { int error=0; app.ActivateDoc3(previous.GetTitle(),false,0,ref error); }
            return jobs;
        }
        public static DrawingOptions ForJob(DrawingOptions source,BatchJob job)
        {
            var json=new JavaScriptSerializer(); var copy=json.Deserialize<DrawingOptions>(json.Serialize(source));
            copy.Configuration=job.Configuration; copy.AllConfigurations=false; return copy;
        }
    }
}
