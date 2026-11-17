using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using AutoDrawing;
using SolidWorks.Interop.sldworks;
class ProbeRack
{
    [STAThread] static void Main(string[] args) {
        var app=(SldWorks)Marshal.GetActiveObject("SldWorks.Application"); var prev=app.ActiveDoc as ModelDoc2; int e=0,w=0;
        var doc=app.GetOpenDocumentByName(args[0]) as ModelDoc2; bool opened=doc==null;
        try {
            if(opened) doc=app.OpenDoc6(args[0],1,3,"",ref e,ref w);
            var report=ModelRecognizer.Read(doc,Console.WriteLine);
            File.WriteAllText(args[1],new JavaScriptSerializer { MaxJsonLength=int.MaxValue }.Serialize(report));
            Console.WriteLine(new JavaScriptSerializer().Serialize(report.RackGeometry));
            foreach(string config in new[]{doc.ConfigurationManager.ActiveConfiguration.Name,""}) {
                var m=doc.Extension.get_CustomPropertyManager(config);
                foreach(string name in m.GetNames() as string[] ?? new string[0]) { string raw,resolved; bool a,b; m.Get6(name,false,out raw,out resolved,out a,out b); Console.WriteLine("PROPERTY "+name+"="+resolved); }
            }
        } finally { if(opened && doc!=null) app.CloseDoc(doc.GetTitle()); if(prev!=null) app.ActivateDoc3(prev.GetTitle(),false,0,ref e); }
    }
}
