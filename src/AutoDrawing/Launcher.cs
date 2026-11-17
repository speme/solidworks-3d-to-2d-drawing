using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;

namespace AutoDrawing
{
    // A separate executable uses the same UI and engine without COM add-in registration.
    internal static class Launcher
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
            try
            {
                SldWorks app;
                try { app=(SldWorks)Marshal.GetActiveObject("SldWorks.Application"); }
                catch(COMException) { app=(SldWorks)Activator.CreateInstance(Type.GetTypeFromProgID("SldWorks.Application")); app.Visible=true; }
                Application.Run(new DrawingForm(app));
            }
            catch(Exception ex) { MessageBox.Show(ex.Message,"AutoDrawing 启动失败",MessageBoxButtons.OK,MessageBoxIcon.Error); }
        }
    }
}
