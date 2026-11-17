using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
class PrepareInferenceGear
{
    [STAThread] static void Main(string[] args) {
        File.Copy(args[0],args[1],false);
        var app=(SldWorks)Marshal.GetActiveObject("SldWorks.Application"); var previous=app.ActiveDoc as ModelDoc2; int e=0,w=0;
        var doc=app.OpenDoc6(args[1],1,1,"",ref e,ref w);
        try {
            var manager=doc.Extension.get_CustomPropertyManager("");
            foreach(string name in new[]{"Module","Teeth","PressureAngle"}) manager.Delete2(name);
            e=0;w=0; if(!doc.Save3(1,ref e,ref w) || e!=0 || w!=0) throw new IOException("Owned fixture save failed "+e+"/"+w);
        } finally { if(doc!=null) app.CloseDoc(doc.GetTitle()); if(previous!=null) app.ActivateDoc3(previous.GetTitle(),false,0,ref e); }
    }
}
