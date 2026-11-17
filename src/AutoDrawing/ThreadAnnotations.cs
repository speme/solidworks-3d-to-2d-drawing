using System;
using System.Collections.Generic;
using System.Linq;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SwView=SolidWorks.Interop.sldworks.View;

namespace AutoDrawing
{
    internal sealed class FeatureNode { internal Feature Feature; internal string Path; }
    internal static class FeatureTraversal
    {
        internal static IEnumerable<FeatureNode> Active(ModelDoc2 model)
        {
            var seen=new HashSet<string>();
            for(var feature=model.FirstFeature() as Feature;feature!=null;feature=feature.GetNextFeature() as Feature)
                foreach(var item in Walk(feature,"",seen,0)) yield return item;
        }
        private static IEnumerable<FeatureNode> Walk(Feature feature,string parent,HashSet<string> seen,int depth)
        {
            if(depth>32 || feature.IsSuppressed()) yield break;
            string path=parent+"/"+feature.Name,identity=feature.GetID()>0?"id:"+feature.GetID():path+":"+feature.GetTypeName2();
            if(!seen.Add(identity)) yield break;
            yield return new FeatureNode { Feature=feature,Path=path };
            for(var child=feature.GetFirstSubFeature() as Feature;child!=null;child=child.GetNextSubFeature() as Feature)
                foreach(var item in Walk(child,path,seen,depth+1)) yield return item;
        }
    }
    public sealed class ThreadSource
    {
        public string FeatureName { get; set; }
        public string FeatureId { get; set; }
        public string FeaturePath { get; set; }
        public string Kind { get; set; }
        public string Callout { get; set; }
        public double NominalDiameterMm { get; set; }
        public double DepthMm { get; set; }
        public int EndCondition { get; set; }
    }
    public sealed class ThreadCalloutEvidence
    {
        public string SourceFeature { get; set; }
        public string SourceFeatureId { get; set; }
        public string View { get; set; }
        public string AnnotationName { get; set; }
        public string Text { get; set; }
        public string Representation { get; set; }
        public double[] Position { get; set; }
        public bool AttachedToCosmeticThread { get; set; }
        public bool Dangling { get; set; }
        public bool Reopened { get; set; }
    }
    internal static class ThreadAnnotations
    {
        internal static void ReadSource(ModelDoc2 model,FeatureNode node,RecognitionReport report)
        {
            string kind=node.Feature.GetTypeName2();
            if(kind!="CosmeticThread" && kind!="Thread") return;
            var source=new ThreadSource { FeatureName=node.Feature.Name,FeaturePath=node.Path,FeatureId=ModelRecognizer.Persist(model,node.Feature),Kind=kind };
            var cosmetic=node.Feature.GetDefinition() as CosmeticThreadFeatureData;
            if(cosmetic!=null)
            {
                source.Callout=cosmetic.ThreadCallout; source.NominalDiameterMm=cosmetic.Diameter*1000;
                source.DepthMm=cosmetic.BlindDepth*1000; source.EndCondition=cosmetic.EndCondition;
                if(string.IsNullOrWhiteSpace(source.Callout) && !string.IsNullOrWhiteSpace(cosmetic.Size)) source.Callout=cosmetic.Size;
            }
            var real=node.Feature.GetDefinition() as ThreadFeatureData;
            if(real!=null) { source.Callout=real.Size; source.NominalDiameterMm=real.Diameter*1000; source.DepthMm=real.BlindDepth*1000; source.EndCondition=real.EndCondition; }
            report.Threads.Add(source);
        }
        private static bool IsThreadAttached(Note note)
        {
            if(note==null || !note.IsAttached()) return false;
            var a=(Annotation)note.GetAnnotation(); var types=a.GetAttachedEntityTypes() as int[];
            return !a.IsDangling() && types!=null && types.Contains((int)swSelectType_e.swSelCTHREADS) && note.GetLeaderCount()>0;
        }
        internal static void Insert(ModelDoc2 model,IList<SwView> views,Box[] cells,JobReport report)
        {
            var drawing=(DrawingDoc)model;
            if(report.Recognition.Threads.Count==0) return;
            model.Extension.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplayCosmeticThreads,0,true);
            model.Extension.SetUserPreferenceToggle((int)swUserPreferenceToggle_e.swDisplayAnnotations,0,true);
            foreach(var source in report.Recognition.Threads)
            {
                if(string.IsNullOrWhiteSpace(source.Callout)) { report.Warnings.Add("螺纹特征没有可读规格，未按直径猜测："+source.FeaturePath); continue; }
                bool done=false;
                for(int i=0;i<Math.Min(3,views.Count) && !done;i++)
                {
                    var view=views[i]; drawing.ActivateView(view.GetName2()); model.ClearSelection2(true);
                    if(!model.Extension.SelectByID2(view.GetName2(),"DRAWINGVIEW",0,0,0,false,0,null,0)) continue;
                    drawing.InsertModelAnnotations3(0,(int)swInsertAnnotation_e.swInsertCThreads,false,true,false,false);
                    for(var ct=view.GetFirstCThread() as CThread;ct!=null;ct=ct.GetNext() as CThread)
                    {
                        var a=ct.GetAnnotation() as Annotation;
                        if(a==null || a.GetName()!=source.FeatureName || a.IsDangling()) continue;
                        Note note=ct.ThreadCallout; string representation="native_thread_callout";
                        if(note==null)
                        {
                            model.ClearSelection2(true);
                            if(!a.Select3(false,null)) continue;
                            drawing.InsertThreadCallout(); model.ForceRebuild3(false); note=ct.ThreadCallout;
                        }
                        if(note==null)
                        {
                            // Preserve the genuine CThread entity. Attach its exact source text to that CThread,
                            // never to a guessed circle/diameter, and explicitly disclose this representation.
                            model.ClearSelection2(true); if(!a.Select3(false,null)) continue;
                            note=model.InsertNote(source.Callout) as Note;
                            representation="cosmetic_thread_with_source_note";
                        }
                        if(note==null) continue;
                        if(!IsThreadAttached(note)) throw new InvalidOperationException("螺纹标注未绑定真实装饰螺纹实体："+source.FeaturePath);
                        var na=(Annotation)note.GetAnnotation(); var cell=cells[i];
                        int inView=report.ThreadCallouts.Count(t=>t.View==view.GetName2());
                        var boundary=(double[])view.GetOutline();
                        double x=Math.Max(cell.X+.012,Math.Min(cell.X+cell.W-.080,boundary[2]+.025));
                        double y=Math.Min(cell.Y+cell.H-.012,boundary[3]+.035+inView*.012);
                        if(y<cell.Y+.015) throw new InvalidOperationException("螺纹标注数量超出当前布局可用范围。");
                        if(!na.SetPosition2(x,y,0)) throw new InvalidOperationException("螺纹引线标注定位失败。");
                        var evidence=new ThreadCalloutEvidence { SourceFeature=source.FeatureName,SourceFeatureId=source.FeatureId,View=view.GetName2(),AnnotationName=na.GetName(),Text=note.GetText(),Representation=representation,AttachedToCosmeticThread=true,Position=(double[])na.GetPosition(),Dangling=na.IsDangling() };
                        if(evidence.Text!=source.Callout) throw new InvalidOperationException("螺纹文字与原生特征不一致："+source.FeaturePath);
                        report.ThreadCallouts.Add(evidence); done=true;
                        if(representation!="native_thread_callout") report.Warnings.Add("原生螺纹文字接口未生成标注，已将原生特征规格用引线绑定同一装饰螺纹，需工程复核："+source.FeatureName);
                        break;
                    }
                }
                if(!done) report.Warnings.Add("螺纹未能在正投影视图中建立标注，禁止视为完整出图："+source.FeaturePath);
            }
            model.ClearSelection2(true); drawing.ActivateView("");
        }
        internal static void Verify(DrawingDoc drawing,JobReport report)
        {
            var sheet=drawing.GetFirstView() as SwView; var all=new List<SwView>();
            for(var view=sheet==null?null:sheet.GetNextView() as SwView;view!=null;view=view.GetNextView() as SwView) all.Add(view);
            foreach(var expected in report.ThreadCallouts)
            {
                var view=all.SingleOrDefault(v=>v.GetName2()==expected.View);
                var notes=view==null?new object[0]:(view.GetNotes() as object[] ?? new object[0]);
                var note=notes.Cast<Note>().SingleOrDefault(n=>((Annotation)n.GetAnnotation()).GetName()==expected.AnnotationName);
                if(note==null || note.GetText()!=expected.Text || !IsThreadAttached(note)) throw new InvalidOperationException("保存重开后螺纹文字或实体关联丢失："+expected.SourceFeature);
                expected.Reopened=true; expected.Position=(double[])((Annotation)note.GetAnnotation()).GetPosition();
            }
        }
    }
}
