using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Web.Script.Serialization;

namespace AutoDrawing
{
    internal static class PdfAudit
    {
        // Windows command-line quoting including embedded quotes and trailing backslashes.
        private static string Quote(string value)
        {
            var text=new StringBuilder("\""); int slash=0;
            foreach(char ch in value)
            {
                if(ch=='\\') { slash++; continue; }
                if(ch=='\"') { text.Append('\\',slash*2+1); text.Append(ch); slash=0; continue; }
                text.Append('\\',slash); slash=0; text.Append(ch);
            }
            text.Append('\\',slash*2); text.Append('"'); return text.ToString();
        }
        public static void Run(string reportPath,JobReport report)
        {
            string root=Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            string script=Path.Combine(root,"scripts","review_pdf.py");
            if(!File.Exists(script)) script=Path.GetFullPath(Path.Combine(root,"..","..","..","..","..","scripts","review_pdf.py"));
            if(!File.Exists(script)) { report.Warnings.Add("未找到 PDF 审查脚本。"); return; }
            var output=new StringBuilder();
            try
            {
                using(var process=new Process { StartInfo=new ProcessStartInfo("python",Quote(script)+" "+Quote(reportPath)) { UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=Encoding.UTF8,StandardErrorEncoding=Encoding.UTF8 } })
                {
                    process.OutputDataReceived+=(s,e)=> { if(e.Data!=null) lock(output) output.AppendLine(e.Data); };
                    process.ErrorDataReceived+=(s,e)=> { if(e.Data!=null) lock(output) output.AppendLine(e.Data); };
                    process.Start(); process.BeginOutputReadLine(); process.BeginErrorReadLine();
                    if(!process.WaitForExit(30000)) { process.Kill(); report.Warnings.Add("PDF 审查超时，需手动复核。"); return; }
                    process.WaitForExit();
                    string audit=Path.ChangeExtension(reportPath,".pdf-review.json");
                    if(process.ExitCode==0 && File.Exists(audit))
                    {
                        report.PdfReview=audit;
                        var evidence=new JavaScriptSerializer { MaxJsonLength=int.MaxValue }.Deserialize<Dictionary<string,object>>(File.ReadAllText(audit));
                        if(!evidence.ContainsKey("all_dimensions_matched") || !Convert.ToBoolean(evidence["all_dimensions_matched"])) report.Warnings.Add("PDF 中存在未关联的尺寸文字，请查看 PDF 审查报告。");
                        if(evidence.ContainsKey("all_thread_callouts_matched") && !Convert.ToBoolean(evidence["all_thread_callouts_matched"])) report.Warnings.Add("PDF 中存在未匹配的螺纹标注文字，请检查 ThreadCallouts 与 PDF 审查报告。");
                        if(evidence.ContainsKey("all_chamfer_callouts_matched") && !Convert.ToBoolean(evidence["all_chamfer_callouts_matched"])) report.Warnings.Add("PDF 中存在未匹配的倒角距离或角度，请检查 ChamferCallouts 与 PDF 审查报告。");
                        var pages=evidence["pages"] as System.Collections.IEnumerable;
                        if(pages!=null && pages.Cast<Dictionary<string,object>>().Any(p=>((System.Collections.IEnumerable)p["collisions"]).Cast<object>().Any())) report.Warnings.Add("PDF 存在文字框碰撞，请复核并调整标注。");
                    }
                    else report.Warnings.Add("PDF 审查未通过/缺少 Python 或 PyMuPDF："+output.ToString().Substring(0,Math.Min(output.Length,1200)));
                }
            }
            catch(Exception ex) { report.Warnings.Add("PDF 审查未执行："+ex.Message); }
        }
    }
}
