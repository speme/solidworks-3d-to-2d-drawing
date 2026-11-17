using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AutoDrawing;
using SolidWorks.Interop.sldworks;
using SwView=SolidWorks.Interop.sldworks.View;
class FindLineOverlaps
{
    [STAThread] static void Main(string[] args)
    {
        var app=(SldWorks)Marshal.GetActiveObject("SldWorks.Application"); var previous=app.ActiveDoc as ModelDoc2; int e=0,w=0;
        var doc=app.OpenDoc6(args[0],3,3,"",ref e,ref w); var list=new List<Tuple<string,int,PaperLine>>();
        try {
            for(var view=((SwView)((DrawingDoc)doc).GetFirstView()).GetNextView() as SwView;view!=null;view=view.GetNextView() as SwView)
            for(var d=view.GetFirstDisplayDimension5();d!=null;d=d.GetNext5()) {
                var data=(DisplayData)d.GetDisplayData(); var name=d.GetDimension2(0).FullName+" T="+d.Type2+" W="+d.WitnessVisibility;
                for(int i=0;i<data.GetLineCount();i++) { var p=(double[])data.GetLineAtIndex3(i); list.Add(Tuple.Create(name,i,new PaperLine(p[4],p[5],p[7],p[8]))); }
            }
            for(int i=0;i<list.Count;i++) for(int j=i+1;j<list.Count;j++) if(list[i].Item1!=list[j].Item1 && LineRules.Overlap(list[i].Item3,list[j].Item3)>.0007) {
                Console.WriteLine(list[i].Item1+" #"+list[i].Item2+" <-> "+list[j].Item1+" #"+list[j].Item2+" mm="+LineRules.Overlap(list[i].Item3,list[j].Item3)*1000);
                var a=list[i].Item3; var b=list[j].Item3; Console.WriteLine(a.X1+","+a.Y1+" -> "+a.X2+","+a.Y2+" / "+b.X1+","+b.Y1+" -> "+b.X2+","+b.Y2);
            }
        } finally { app.CloseDoc(doc.GetTitle()); if(previous!=null) app.ActivateDoc3(previous.GetTitle(),false,0,ref e); }
    }
}
