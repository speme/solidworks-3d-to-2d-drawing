using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
class ProbeStyle
{
    [STAThread] static void Main()
    {
        var app=(SldWorks)Marshal.GetActiveObject("SldWorks.Application"); var prev=app.ActiveDoc as ModelDoc2;
        var doc=app.NewDocument(@"C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2026\templates\gb_a4.drwdot",0,0,0) as ModelDoc2;
        try { foreach(int key in new[]{53,63,517}) {
            int value=key==53?2:0;
            Console.WriteLine(key+" before="+doc.Extension.GetUserPreferenceInteger(key,0)+" set="+doc.Extension.SetUserPreferenceInteger(key,0,value)+" after="+doc.Extension.GetUserPreferenceInteger(key,0));
            Console.WriteLine("legacy="+doc.GetUserPreferenceIntegerValue(key)+" set="+doc.SetUserPreferenceIntegerValue(key,value)+" after="+doc.GetUserPreferenceIntegerValue(key));
        } } finally { app.CloseDoc(doc.GetTitle()); if(prev!=null) { int e=0; app.ActivateDoc3(prev.GetTitle(),false,0,ref e); } }
    }
}
