using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using AutoDrawing;
using SolidWorks.Interop.sldworks;

// Runs the real form/button/message-loop path with isolated settings and output files.
class UiNativeRegression
{
    static FieldInfo Field(string name) { return typeof(DrawingForm).GetField(name,BindingFlags.Instance|BindingFlags.NonPublic); }
    [STAThread] static int Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        var json=new JavaScriptSerializer { MaxJsonLength=int.MaxValue };
        var request=json.Deserialize<GenerationRequest>(File.ReadAllText(args[0]));
        var options=json.Deserialize<DrawingOptions>(json.Serialize(request.Options));
        Directory.CreateDirectory(options.OutputDirectory);
        var app=(SldWorks)Marshal.GetActiveObject("SldWorks.Application");
        using(var form=new DrawingForm(app)) {
            Field("settings").SetValue(form,Path.Combine(options.OutputDirectory,"test-ui-settings.json"));
            typeof(DrawingForm).GetMethod("Apply",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(form,new object[] { options });
            var files=(ListBox)Field("files").GetValue(form);
            foreach(var model in request.Models) files.Items.Add(model.Path);
            bool started=false; var timer=new Timer { Interval=200 };
            form.Shown+=(s,e)=> { started=true; ((Button)Field("run").GetValue(form)).PerformClick(); };
            timer.Tick+=(s,e)=> {
                if(!started || (bool)Field("running").GetValue(form)) return;
                var reports=Directory.GetFiles(options.OutputDirectory,"batch_*.json");
                if(reports.Length==0) return;
                timer.Stop(); File.WriteAllText(Path.Combine(options.OutputDirectory,"test-ui.log"),((TextBox)Field("log").GetValue(form)).Text); form.Close();
            };
            timer.Start(); Application.Run(form); timer.Dispose();
        }
        string report=Directory.GetFiles(options.OutputDirectory,"batch_*.json").OrderByDescending(File.GetLastWriteTimeUtc).First();
        var batch=json.Deserialize<BatchSummary>(File.ReadAllText(report));
        Console.WriteLine(report);
        return batch.Failed==0 && batch.Jobs.Count==request.Models.Count?0:1;
    }
}
