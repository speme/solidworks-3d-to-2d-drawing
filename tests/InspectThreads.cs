using System;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
class InspectThreads
{
    static void Tree(Feature f,int level)
    {
        if(level>12) return;
        Console.WriteLine(new string(' ',level*2)+f.Name+" | "+f.GetTypeName2()+" | suppressed="+f.IsSuppressed());
        if(f.GetTypeName2().ToLowerInvariant().Contains("thread"))
        {
            var cosmetic=f.GetDefinition() as CosmeticThreadFeatureData;
            if(cosmetic!=null) {
                Console.WriteLine("COSMETIC callout="+cosmetic.ThreadCallout+" size="+cosmetic.Size+" depth="+cosmetic.BlindDepth+" dia="+cosmetic.Diameter+" end="+cosmetic.EndCondition+" standard="+cosmetic.Standard);
                var edge=cosmetic.Edge; if(edge!=null) { var curve=edge.GetCurve() as Curve; if(curve!=null && curve.IsCircle()) Console.WriteLine("EDGE circle="+string.Join(",",(double[])curve.CircleParams)); }
            }
            var thread=f.GetDefinition() as ThreadFeatureData;
            if(thread!=null) Console.WriteLine("THREAD size="+thread.Size+" type="+thread.Type+" pitch="+thread.Pitch+" depth="+thread.BlindDepth);
        }
        for(var sub=f.GetFirstSubFeature() as Feature;sub!=null;sub=sub.GetNextSubFeature() as Feature) Tree(sub,level+1);
    }
    [STAThread]
    static int Main(string[] args)
    {
        Console.OutputEncoding=new System.Text.UTF8Encoding(false);
        var app=(SldWorks)Marshal.GetActiveObject("SldWorks.Application"); var previous=app.ActiveDoc as ModelDoc2;
        var doc=app.GetOpenDocumentByName(args[0]) as ModelDoc2; bool opened=doc==null;
        try
        {
            if(opened) { int e=0,w=0; doc=app.OpenDoc6(args[0],1,3,"",ref e,ref w); }
            Console.WriteLine("DOC "+doc.GetPathName()+" dirty="+doc.GetSaveFlag());
            foreach(string config in new[]{"",doc.ConfigurationManager.ActiveConfiguration.Name})
            {
                var props=doc.Extension.get_CustomPropertyManager(config); var names=props.GetNames() as string[] ?? new string[0];
                foreach(string name in names) { string raw,resolved; bool yes,linked; props.Get6(name,false,out raw,out resolved,out yes,out linked); Console.WriteLine("PROP ["+config+"] "+name+" = "+raw+" / "+resolved); }
            }
            for(var f=doc.FirstFeature() as Feature;f!=null;f=f.GetNextFeature() as Feature) Tree(f,0);
        }
        finally { if(opened && doc!=null) app.CloseDoc(doc.GetTitle()); if(previous!=null) { int e=0; app.ActivateDoc3(previous.GetTitle(),false,0,ref e); } }
        return 0;
    }
}
