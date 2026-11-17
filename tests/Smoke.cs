using System;
using System.IO;
using System.Runtime.InteropServices;
using AutoDrawing;
using SolidWorks.Interop.sldworks;
class Smoke
{
    [STAThread]
    static int Main(string[] args)
    {
        SldWorks app;
        try { app=(SldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
        catch(COMException) { app=(SldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application")); app.Visible=true; }
        var options=new DrawingOptions { Template=args[1],OutputDirectory=args[2],AutoDimension=true,ImportDimensions=true };
        var report=new DrawingEngine(app).Run(args[0],options,Console.WriteLine);
        Console.WriteLine(report.Status); Console.WriteLine(report.Error);
        foreach(var w in report.Warnings) Console.WriteLine(w);
        return report.Status=="failed"?1:0;
    }
}
