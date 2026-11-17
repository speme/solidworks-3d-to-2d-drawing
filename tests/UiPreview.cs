using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using AutoDrawing;
using SolidWorks.Interop.sldworks;
class UiPreview
{
    [STAThread]
    static void Main(string[] args)
    {
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        SldWorks app;
        try { app=(SldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
        catch(COMException) { app=(SldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application")); }
        using(var form=new DrawingForm(app))
        {
            form.CreateControl(); form.Show(); Application.DoEvents();
            using(var bitmap=new Bitmap(form.Width,form.Height)) { form.DrawToBitmap(bitmap,new Rectangle(0,0,form.Width,form.Height)); bitmap.Save(args[0]); }
            form.Close();
        }
        if(args.Length>1)
        using(var editor=new TemplateEditorForm(new DrawingOptions { Template=app.GetDocumentTemplate(3,"",0,0,0),OutputDirectory=System.IO.Path.GetTempPath() }))
        {
            editor.Show(); Application.DoEvents();
            using(var bitmap=new Bitmap(editor.Width,editor.Height)) { editor.DrawToBitmap(bitmap,new Rectangle(0,0,editor.Width,editor.Height)); bitmap.Save(args[1]); }
            editor.Close();
        }
    }
}
