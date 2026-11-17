using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
class InspectLabels
{
    [STAThread]
    static void Main(string[] args)
    {
        var app=(SldWorks)Marshal.GetActiveObject("SldWorks.Application"); var previous=app.ActiveDoc as ModelDoc2;
        var doc=app.GetOpenDocumentByName(args[0]) as ModelDoc2; bool opened=doc==null; int e=0,w=0;
        try {
            if(opened) doc=app.OpenDoc6(args[0],3,3,"",ref e,ref w);
            var drawing=(DrawingDoc)doc; var sheet=drawing.GetFirstView() as SolidWorks.Interop.sldworks.View;
            for(var v=sheet.GetNextView() as SolidWorks.Interop.sldworks.View;v!=null;v=v.GetNextView() as SolidWorks.Interop.sldworks.View)
            for(var dd=v.GetFirstDisplayDimension5();dd!=null;dd=dd.GetNext5()) {
                var a=(Annotation)dd.GetAnnotation(); var data=(DisplayData)dd.GetDisplayData();
                Console.WriteLine(v.GetName2()+" "+dd.GetDimension2(0).SystemValue+" A="+string.Join(",",(double[])a.GetPosition()));
                for(int i=0;i<data.GetTextCount();i++) Console.WriteLine("  "+data.GetTextAtIndex(i)+" h="+data.GetTextHeightAtIndex(i)+" p="+string.Join(",",(double[])data.GetTextPositionAtIndex(i))+" angle="+data.GetTextAngleAtIndex(i));
            }
        } finally { if(opened && doc!=null) app.CloseDoc(doc.GetTitle()); if(previous!=null) app.ActivateDoc3(previous.GetTitle(),false,0,ref e); }
    }
}
