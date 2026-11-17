using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swpublished;
using SolidWorks.Interop.swconst;

[assembly: System.Reflection.AssemblyTitle("AutoDrawing 自动出图")]
[assembly: System.Reflection.AssemblyVersion("1.0.0.0")]
[assembly: ComVisible(false)]

namespace AutoDrawing
{
    [ComVisible(true), Guid("B738EE37-3079-4CBA-9DC3-9A70AB9CB164"), ProgId("AutoDrawing.SwAddin")]
    [ClassInterface(ClassInterfaceType.AutoDispatch)]
    public sealed class SwAddin : ISwAddin
    {
        private SldWorks app;
        private CommandManager commands;
        private DrawingForm form;
        public bool ConnectToSW(object ThisSW, int Cookie)
        {
            app=(SldWorks)ThisSW;
            if (!app.SetAddinCallbackInfo2(0,this,Cookie)) return false;
            commands=app.GetCommandManager(Cookie);
            int error=0;
            var group=commands.CreateCommandGroup2(78124,"自动出图","3D 转 2D 与自动标注","自动出图",-1,true,ref error);
            if (group==null) return false;
            group.AddCommandItem2("批量出图",-1,"配置三视图、标注和输出","批量出图",-1,"ShowPanel","",1,(int)swCommandItemType_e.swMenuItem);
            group.HasMenu=true;
            return group.Activate();
        }
        public void ShowPanel()
        {
            if (form==null || form.IsDisposed) form=new DrawingForm(app);
            form.Show(); form.Activate();
        }
        public bool DisconnectFromSW()
        {
            if (form!=null) form.Dispose();
            if (commands!=null) commands.RemoveCommandGroup(78124);
            form=null; commands=null; app=null;
            return true;
        }
    }
}
