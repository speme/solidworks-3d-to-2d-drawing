using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SwView=SolidWorks.Interop.sldworks.View;
class ProbeRackExport
{
    [STAThread] static void Main(string[] args) {
        var app=(SldWorks)Marshal.GetActiveObject("SldWorks.Application"); var prev=app.ActiveDoc as ModelDoc2; int e=0,w=0;
        string copy=args[1]+".slddrw"; File.Copy(args[0],copy,false);
        var doc=app.OpenDoc6(copy,3,3,"",ref e,ref w);
        try {
            app.ActivateDoc3(doc.GetTitle(),false,0,ref e);
            for(int phase=0;phase<4;phase++) {
                for(var v=((DrawingDoc)doc).GetFirstView() as SwView;v!=null;v=v.GetNextView() as SwView)
                for(var table=v.GetFirstTableAnnotation() as TableAnnotation;table!=null;) {
                    var next=table.GetNext() as TableAnnotation;
                    if(phase==3) { doc.ClearSelection2(true); ((Annotation)table.GetAnnotation()).Select3(false,null); doc.Extension.DeleteSelection2(0); }
                    else if(phase>0) for(int r=0;r<table.RowCount;r++) for(int c=0;c<table.ColumnCount;c++) table.set_Text(r,c,phase==1?table.get_Text(r,c).Replace("⌀","Φ"):"Cell "+r+"-"+c);
                    table=next;
                }
                doc.ClearSelection2(true); ((DrawingDoc)doc).ActivateView(""); doc.ForceRebuild3(false); doc.GraphicsRedraw2();
                e=0; w=0; bool saved=doc.Extension.SaveAs(args[1]+"-"+phase+".pdf",0,1,null,ref e,ref w);
                Console.WriteLine("PHASE "+phase+" "+saved+" "+e+"/"+w);
            }
        } finally { if(doc!=null) app.CloseDoc(doc.GetTitle()); if(prev!=null) app.ActivateDoc3(prev.GetTitle(),false,0,ref e); }
    }
}
