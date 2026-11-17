using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
class ProbeSheet
{
    [STAThread]
    static void Main()
    {
        var app=(SldWorks)Marshal.GetActiveObject("SldWorks.Application");
        var previous=app.ActiveDoc as ModelDoc2;
        try
        {
            foreach(string name in new[]{"gb_a0","gb_a3"}) foreach(bool legacy in new[]{true,false})
            {
                ModelDoc2 model=null;
                try
                {
                    model=app.NewDocument(@"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2026\templates\"+name+".drwdot",0,0,0) as ModelDoc2;
                    var drawing=(DrawingDoc)model; var sheet=(Sheet)drawing.GetCurrentSheet();
                    var props=(double[])sheet.GetProperties2(); double width=0,height=0; sheet.GetSize(ref width,ref height);
                    string format=sheet.GetTemplateName(); var sketch=sheet.GetTemplateSketch() as Sketch;
                    int count=sketch==null?-1:((object[])sketch.GetSketchSegments()).Length;
                    Console.WriteLine(name+" legacy="+legacy+" props=["+string.Join(",",props)+"] format="+format+" exists="+File.Exists(format)+" segments="+count);
                    if(legacy) Console.WriteLine("SetupSheet5="+drawing.SetupSheet5(sheet.GetName(),(int)props[0],(int)props[1],1,1,true,format,width,height,"",false));
                    else sheet.SetProperties2((int)props[0],(int)props[1],1,1,true,width,height,props[7]!=0);
                    model.ForceRebuild3(false); var after=(double[])sheet.GetProperties2();
                    sketch=sheet.GetTemplateSketch() as Sketch;
                    Console.WriteLine("after=["+string.Join(",",after)+"] format="+sheet.GetTemplateName()+" segments="+(sketch==null?-1:((object[])sketch.GetSketchSegments()).Length));
                }
                finally { if(model!=null) app.CloseDoc(model.GetTitle()); }
            }
        }
        finally { if(previous!=null) { int error=0; app.ActivateDoc3(previous.GetTitle(),false,0,ref error); } }
    }
}
