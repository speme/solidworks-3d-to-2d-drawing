using System;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SwView=SolidWorks.Interop.sldworks.View;
class ProbeGearRender
{
    [STAThread] static void Main(string[] args)
    {
        var app=(SldWorks)Marshal.GetActiveObject("SldWorks.Application"); var prev=app.ActiveDoc as ModelDoc2; int e=0,w=0;
        var doc=app.OpenDoc6(args[0],3,3,"",ref e,ref w); var drawing=(DrawingDoc)doc;
        try {
            Console.WriteLine("OPEN "+e+"/"+w);
            for(var v=drawing.GetFirstView() as SwView;v!=null;v=v.GetNextView() as SwView)
            for(var table=v.GetFirstTableAnnotation() as TableAnnotation;table!=null;table=table.GetNext() as TableAnnotation) {
                var f=table.GetTextFormat(); Console.WriteLine("TABLE "+table.RowCount+"x"+table.ColumnCount+" font="+f.TypeFaceName+" size="+f.CharHeight+" pos="+string.Join(",",(double[])((Annotation)table.GetAnnotation()).GetPosition()));
                Console.WriteLine("COLS "+table.GetColumnWidth(0)+"/"+table.GetColumnWidth(1)+" ROW="+table.GetRowHeight(0));
                f.TypeFaceName="Arial"; table.SetTextFormat(false,f);
                foreach(string font in new[]{"SimSun","宋体","Microsoft YaHei"}) {
                    f.TypeFaceName=font; table.SetTextFormat(false,f);
                    for(int row=0;row<table.RowCount;row++) for(int col=0;col<2;col++) table.SetCellTextFormat(row,col,false,f);
                    doc.ForceRebuild3(false); Console.WriteLine("CJK "+font+" "+doc.Extension.SaveAs(System.IO.Path.ChangeExtension(args[1],font+".pdf"),0,1,null,ref e,ref w)+" "+e+"/"+w);
                }
                for(int row=0;row<table.RowCount;row++) { table.set_Text(row,0,"Parameter "+row); table.set_Text(row,1,"Value "+row); }
            }
            doc.ForceRebuild3(false); doc.ViewZoomtofit2();
            Console.WriteLine("PDF "+doc.Extension.SaveAs(args[1],0,1,null,ref e,ref w)+" "+e+"/"+w);
            Console.WriteLine("DXF "+doc.Extension.SaveAs(System.IO.Path.ChangeExtension(args[1],".dxf"),0,1,null,ref e,ref w)+" "+e+"/"+w);
            for(var v=drawing.GetFirstView() as SwView;v!=null;v=v.GetNextView() as SwView) {
                var table=v.GetFirstTableAnnotation() as TableAnnotation;
                if(table!=null) { doc.ClearSelection2(true); ((Annotation)table.GetAnnotation()).Select3(false,null); doc.Extension.DeleteSelection2(0); }
            }
            doc.ClearSelection2(true); drawing.ActivateView(""); doc.ForceRebuild3(false);
            Console.WriteLine("PDF NO TABLE "+doc.Extension.SaveAs(System.IO.Path.ChangeExtension(args[1],".no-table.pdf"),0,1,null,ref e,ref w)+" "+e+"/"+w);
        } finally { app.CloseDoc(doc.GetTitle()); if(prev!=null) app.ActivateDoc3(prev.GetTitle(),false,0,ref e); }
    }
}
