using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Diagnostics;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;
using SwView = SolidWorks.Interop.sldworks.View;

namespace AutoDrawing
{
    public sealed class DrawingEngine
    {
        private readonly SldWorks app;
        private readonly int thread;
        public DrawingEngine(SldWorks app) { this.app=app; thread=Thread.CurrentThread.ManagedThreadId; }
        private static void Rebuild(ModelDoc2 doc)
        { if(!doc.ForceRebuild3(false)) throw new InvalidOperationException("工程图重建失败。"); }
        private static string SavedSignature(DimensionEvidence d)
        { return string.Join("|",new[]{d.View,d.Model,d.Configuration,d.Type.ToString(),d.ValueVerified?d.ValueSI.ToString("G12",System.Globalization.CultureInfo.InvariantCulture):"unverified:"+d.DisplayText,d.Dangling.ToString(),d.ChamferAngleDegrees.ToString("G12",System.Globalization.CultureInfo.InvariantCulture),d.ChamferTextStyle.ToString()}); }
        private static string ViewName(ModelDoc2 model,string english,string chinese)
        {
            var available=(string[])model.GetModelViewNames();
            string match=available.FirstOrDefault(n=>n.Equals(english,StringComparison.OrdinalIgnoreCase) || n==chinese);
            if(match==null) throw new InvalidOperationException("无法解析本机标准视图："+english+"。支持中文和英文 SolidWorks。");
            return match;
        }
        private static string StandardView(ModelDoc2 model,string view)
        { return ViewName(model,"*"+view,view=="Top"?"*上视":view=="Right"?"*右视":view=="Left"?"*左视":view=="Back"?"*后视":view=="Bottom"?"*下视":"*前视"); }
        private static int DimensionCount(SwView view)
        { int count=0; for(var d=view.GetFirstDisplayDimension5();d!=null;d=d.GetNext5()) count++; return count; }
        private static int Marks(IEnumerable<SwView> views)
        { int count=0; foreach(var v in views) for(var mark=v.GetFirstCenterMark2() as CenterMark;mark!=null;mark=mark.GetNext()) count++; return count; }
        private static void CheckProperties(ModelDoc2 drawing,Dictionary<string,string> expected)
        {
            var manager=drawing.Extension.get_CustomPropertyManager("");
            foreach(var property in expected)
            {
                string raw,resolved; bool wasResolved,linked;
                manager.Get6(property.Key,false,out raw,out resolved,out wasResolved,out linked);
                if(raw!=property.Value) throw new InvalidOperationException("工程图自定义属性回读失败："+property.Key);
            }
        }
        private sealed class DimItem
        {
            public DisplayDimension Display;
            public Annotation Annotation;
            public DimensionEvidence Evidence;
        }
        private static string OverallIdentity(SwView view,DisplayDimension display,Dimension dimension)
        {
            // Restricted proof: a reference dimension of ONE body spanning both extrema along ONE model axis.
            // Parallel hole distances cannot satisfy this unless they actually span the body's outer envelope.
            var model=view.ReferencedDocument;
            if(model==null || model.GetType()!=1 || model.ConfigurationManager.ActiveConfiguration.Name!=view.ReferencedConfiguration || !display.IsReferenceDim() || display.Type2!=2 || dimension.GetToleranceType()!=0) return null;
            if(!string.IsNullOrEmpty(display.GetText(1)) || !string.IsNullOrEmpty(display.GetText(2)) || !string.IsNullOrEmpty(display.GetText(0))) return null;
            var bodies=((PartDoc)model).GetBodies2(0,false) as object[];
            if(bodies==null || bodies.Length!=1) return null;
            var points=dimension.ReferencePoints as object[];
            if(points==null || points.Length<2 || dimension.DimensionLineDirection==null) return null;
            var dir=(double[])dimension.DimensionLineDirection.ArrayData;
            int axis=Array.FindIndex(dir,x=>Math.Abs(x)>1-1e-9);
            if(axis<0 || axis>2 || dir.Where((x,i)=>i!=axis).Any(x=>Math.Abs(x)>1e-9)) return null;
            var a=(double[])((MathPoint)points[0]).ArrayData; var b=(double[])((MathPoint)points[1]).ArrayData;
            var box=(double[])((Body2)bodies[0]).GetBodyBox();
            if(Math.Abs(Math.Min(a[axis],b[axis])-box[axis])>1e-8 || Math.Abs(Math.Max(a[axis],b[axis])-box[axis+3])>1e-8) return null;
            var bodyId=model.Extension.GetPersistReference3(bodies[0]) as byte[];
            if(bodyId==null || bodyId.Length==0) return null;
            return "overall:"+Convert.ToBase64String(bodyId)+":"+axis+":"+box[axis].ToString("R",System.Globalization.CultureInfo.InvariantCulture)+":"+box[axis+3].ToString("R",System.Globalization.CultureInfo.InvariantCulture);
        }
        private static IEnumerable<SwView> Views(DrawingDoc drawing)
        {
            var sheet=(SwView)drawing.GetFirstView();
            for(var v=sheet==null?null:(SwView)sheet.GetNextView();v!=null;v=(SwView)v.GetNextView()) yield return v;
        }
        private static List<DimItem> ReadDimensions(DrawingDoc drawing)
        {
            var result=new List<DimItem>();
            foreach(var view in Views(drawing))
            for(var display=(DisplayDimension)view.GetFirstDisplayDimension5();display!=null;display=display.GetNext5())
            {
                var dimension=display.GetDimension2(0);
                var annotation=(Annotation)display.GetAnnotation();
                if (dimension==null || annotation==null) continue;
                // A persistent model-dimension reference distinguishes assembly occurrences and drawing reference dimensions.
                string identity="";
                string geometryKey=null;
                try
                {
                    var model=view.ReferencedDocument;
                    var bytes=model==null?null:model.Extension.GetPersistReference3(dimension) as byte[];
                    if(bytes!=null && bytes.Length>0) identity=Convert.ToBase64String(bytes);
                    geometryKey=OverallIdentity(view,display,dimension);
                    if(geometryKey!=null) identity=geometryKey;
                }
                catch(COMException) { /* Unknown lineage must remain untouched. */ }
                var evidence=new DimensionEvidence {
                    View=view.GetName2(), Model=view.GetReferencedModelName(), Configuration=view.ReferencedConfiguration,
                    FullName=dimension.FullName, Identity=identity, Dangling=annotation.IsDangling(),
                    GeometryKey=geometryKey,ValueSI=dimension.SystemValue,Type=display.Type2,
                    Entities=((annotation.GetAttachedEntities3() as object[])??new object[0]).Select(e=>ModelRecognizer.PersistFromView(view.ReferencedDocument,e)).Where(e=>e!="").Distinct().ToList(),
                    Semantic=display.Type2+"|"+display.IsReferenceDim()+"|"+display.GetText(0)+"|"+dimension.GetToleranceType()+"|"+string.Join(",",((double[])dimension.GetToleranceValues())??new double[0]),
                    Position=(double[])annotation.GetPosition()
                };
                if(display.Type2==10) {
                    double length=0,angle=0;
                    bool verified=false;
                    try { verified=ChamferAnnotations.Values(display,out length,out angle); } catch(COMException) { }
                    if(!verified) { evidence.ValueVerified=false; evidence.DisplayText=ChamferAnnotations.Text(display); }
                    else { evidence.ValueSI=length; evidence.ChamferAngleDegrees=angle*180/Math.PI; }
                    evidence.ChamferTextStyle=display.ChamferTextStyle;
                }
                result.Add(new DimItem { Display=display,Annotation=annotation,Evidence=evidence });
            }
            return result;
        }
        private static void Deduplicate(ModelDoc2 doc, DrawingDoc drawing, JobReport report)
        {
            var seen=new HashSet<string>(StringComparer.Ordinal);
            foreach(var item in ReadDimensions(drawing))
            {
                string key=DimensionRules.Key(item.Evidence);
                if(key==null || seen.Add(key)) continue;
                doc.ClearSelection2(true);
                if(!item.Annotation.Select3(false,null) || !doc.Extension.DeleteSelection2(0))
                    report.Warnings.Add("重复尺寸删除失败："+item.Evidence.FullName);
                else report.RemovedDuplicates++;
            }
            doc.ClearSelection2(true);
        }
        private void Save(ModelDoc2 doc,string path)
        {
            int activationError=0; app.ActivateDoc3(doc.GetTitle(),false,0,ref activationError);
            if(activationError!=0) throw new IOException("保存前激活工程图失败："+activationError);
            doc.ClearSelection2(true);
            int error=0,warning=0;
            bool saved=doc.Extension.SaveAs(path,(int)swSaveAsVersion_e.swSaveAsCurrentVersion,(int)swSaveAsOptions_e.swSaveAsOptions_Silent,null,ref error,ref warning);
            if((!saved || error!=0) && !path.EndsWith(".slddrw",StringComparison.OrdinalIgnoreCase)) {
                doc.ClearSelection2(true); ((DrawingDoc)doc).ActivateView(""); Rebuild(doc); doc.GraphicsRedraw2();
                error=0; warning=0; saved=doc.Extension.SaveAs(path,(int)swSaveAsVersion_e.swSaveAsCurrentVersion,(int)swSaveAsOptions_e.swSaveAsOptions_Silent,null,ref error,ref warning);
            }
            if(!saved || error!=0 || !File.Exists(path) || new FileInfo(path).Length==0)
                throw new IOException("保存失败："+path+"，错误 "+error+"，警告 "+warning);
            if(warning!=0) throw new IOException("保存返回警告，需复核："+path+"，警告 "+warning);
        }
        public JobReport Run(string path,DrawingOptions options,Action<string> progress)
        {
            if(Thread.CurrentThread.ManagedThreadId!=thread || Thread.CurrentThread.GetApartmentState()!=ApartmentState.STA) throw new InvalidOperationException("SolidWorks 必须在创建执行器的 STA 线程调用。");
            using(var mutex=new Mutex(false,"Local\\AutoDrawing.SolidWorks."+app.GetProcessID()))
            {
                bool held;
                try { held=mutex.WaitOne(0); } catch(AbandonedMutexException) { held=true; }
                if(!held) throw new InvalidOperationException("此 SolidWorks 会话正在执行其他 AutoDrawing 任务，请等待完成。");
                try { return RunCore(path,options,progress??(s=>{})); }
                finally { mutex.ReleaseMutex(); }
            }
        }
        private JobReport RunCore(string path,DrawingOptions options,Action<string> progress)
        {
            if(Thread.CurrentThread.ManagedThreadId!=thread) throw new InvalidOperationException("SolidWorks 调用必须在创建执行器的 STA 线程执行。");
            options.Validate();
            string full=Path.GetFullPath(path);
            if(!File.Exists(full)) throw new FileNotFoundException("模型不存在",full);
            string ext=Path.GetExtension(full).ToLowerInvariant();
            if(ext!=".sldprt" && ext!=".sldasm") throw new ArgumentException("仅支持 SLDPRT/SLDASM。");
            Directory.CreateDirectory(options.OutputDirectory);
            string stem=Path.GetFileNameWithoutExtension(full)+"_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")+"_"+Guid.NewGuid().ToString("N").Substring(0,6);
            string target=Path.Combine(Path.GetFullPath(options.OutputDirectory),stem+".slddrw");
            var originalProgress=progress;
            progress=message=> { File.AppendAllText(Path.ChangeExtension(target,".progress.log"),DateTime.Now.ToString("HH:mm:ss.fff")+" "+message+System.Environment.NewLine,System.Text.Encoding.UTF8); originalProgress(message); };
            var report=new JobReport { Model=full,Drawing=target,Options=options,Revision=app.RevisionNumber() };
            ModelDoc2 previous=(ModelDoc2)app.ActiveDoc,model=null,doc=null;
            string originalConfig=null;
            var timer=Stopwatch.StartNew(); string currentStage="打开模型"; long checkpoint=0;
            Action<string> stage=name=> { report.Stages.Add(new StageEvidence { Name=currentStage,ElapsedMs=timer.ElapsedMilliseconds-checkpoint }); checkpoint=timer.ElapsedMilliseconds; currentStage=name; progress(name); };
            bool opened=false,completed=false;
            try
            {
                progress("运行版本："+RuntimeInfo.Version+"；加载路径："+RuntimeInfo.AssemblyPath);
                progress("执行进程："+Process.GetCurrentProcess().Id+"；SolidWorks 进程："+app.GetProcessID());
                progress("读取 "+Path.GetFileName(full));
                model=app.GetOpenDocumentByName(full) as ModelDoc2;
                if(model==null)
                {
                    int e=0,w=0;
                    model=app.OpenDoc6(full,ext==".sldprt"?1:2,(int)swOpenDocOptions_e.swOpenDocOptions_Silent | (int)swOpenDocOptions_e.swOpenDocOptions_ReadOnly,"",ref e,ref w);
                    opened=model!=null;
                    if(model==null || e!=0) throw new InvalidOperationException("打开模型失败："+e);
                    if(w!=0) report.Warnings.Add("打开模型警告："+w);
                }
                string config=string.IsNullOrWhiteSpace(options.Configuration)?model.ConfigurationManager.ActiveConfiguration.Name:options.Configuration;
                if(model.GetSaveFlag()) throw new InvalidOperationException("模型有未保存修改，请先保存模型后再出图。");
                if(model.GetConfigurationByName(config)==null) throw new InvalidOperationException("配置不存在："+config);
                report.Configuration=config; originalConfig=model.ConfigurationManager.ActiveConfiguration.Name;
                if(config!=originalConfig && !model.ShowConfiguration2(config)) throw new InvalidOperationException("配置切换失败："+config);
                stage("识别几何与特征");
                if(options.GearInference!=null) {
                    report.GearingAnalysis=GearingCalculator.Calculate(options.GearInference);
                    if(report.GearingAnalysis.Status=="conflict") throw new InvalidOperationException("原图齿轮尺寸约束冲突："+string.Join("；",report.GearingAnalysis.Issues));
                    foreach(string issue in report.GearingAnalysis.Issues) report.Warnings.Add(issue);
                    if(report.GearingAnalysis.Missing.Count>0) report.Warnings.Add("原图公式尚缺约束："+string.Join("、",report.GearingAnalysis.Missing));
                    if(options.GearInference.Family=="worm_pair") throw new NotSupportedException("蜗杆蜗轮副目前支持 calculate-gearing 公式计算；尚未验证其自动制造标注，请勿按普通圆柱齿轮生成制造图。");
                }
                report.Recognition=ModelRecognizer.Read(model,progress);
                report.Rack=RackDrawing.Read(model,options,report.Recognition);
                if(report.Rack!=null) report.GearingAnalysis=report.Rack.Calculation;
                report.Gear=report.Rack==null?GearDrawing.Read(model,options,report.Recognition):null;
                if(report.Gear!=null) { report.Recognition.PartClass=report.Gear.Status=="gear_parameters_required"?"gear_candidate":"gear"; report.Recognition.PrimaryView=GearDrawing.Primary(report.Recognition); }
                if(report.Rack!=null) { report.Recognition.PartClass=report.Rack.Status=="rack_parameters_required"?"rack_candidate":"rack"; report.Recognition.PrimaryView=new[]{"Right","Top","Front"}[report.Rack.Geometry.WidthAxis]; progress("齿条识别：实测节距 "+report.Rack.Geometry.MeasuredPitchMm.ToString("0.###")+" mm，跳过逐齿自动标注"); }
                if(!options.AutoOrient) report.Recognition.PrimaryView="Front";
                stage("创建视图与布局 · "+report.Recognition.PrimaryView);
                doc=app.NewDocument(Path.GetFullPath(options.Template),0,0,0) as ModelDoc2;
                if(doc==null) throw new InvalidOperationException("无法从模板创建工程图。");
                var drawing=(DrawingDoc)doc;
                if(Views(drawing).Any()) throw new InvalidOperationException("模板含有预定义视图，请使用空视图工程图模板。");
                if(((string[])drawing.GetSheetNames()).Length!=1) throw new InvalidOperationException("请使用单页模板。");
                var sheet=(Sheet)drawing.GetCurrentSheet();
                SheetSetup.Apply(doc,options,report);
                double width=report.SheetSizeMm[0]/1000,height=report.SheetSizeMm[1]/1000,readWidth=0,readHeight=0;
                report.DrawingProperties=new Dictionary<string,string>();
                foreach(var property in options.DrawingProperties)
                {
                    string value=property.Value.Replace("{ModelName}",Path.GetFileNameWithoutExtension(full)).Replace("{Configuration}",config).Replace("{Date}",DateTime.Now.ToString("yyyy-MM-dd"));
                    int code=doc.Extension.get_CustomPropertyManager("").Add3(property.Key,(int)swCustomInfoType_e.swCustomInfoText,value,(int)swCustomPropertyAddOption_e.swCustomPropertyDeleteAndAdd);
                    if(code!=(int)swCustomInfoAddResult_e.swCustomInfoAddResult_AddedOrChanged) throw new InvalidOperationException("自定义属性写入失败："+property.Key+"，状态 "+code);
                    report.DrawingProperties[property.Key]=value;
                }
                CheckProperties(doc,report.DrawingProperties);
                Box[] cells=report.Rack!=null?RackDrawing.Cells(width,height,options):LayoutPlanner.Cells(width,height,options.FirstAngle,options.TitleBlockHeightMm/1000,options.LeftMarginMm/1000,options.RightMarginMm/1000,options.TopMarginMm/1000,options.ViewGapMm/1000);
                Box gearTableArea=null;
                if(report.Gear!=null) { var original=cells[2]; gearTableArea=new Box(original.X,original.Y,original.W*.65,original.H); cells[2]=GearDrawing.Reserve(original,report.Gear); }
                var created=new List<SwView>();
                var bounds=new List<Tuple<double,double,Box>>();
                for(int i=0;i<(options.Isometric?4:3);i++)
                {
                    var cell=cells[i];
                    SwView view;
                    if(i==1 || i==2)
                    {
                        doc.ClearSelection2(true);
                        if(!doc.Extension.SelectByID2(created[0].GetName2(),"DRAWINGVIEW",0,0,0,false,0,null,0)) throw new InvalidOperationException("无法选择投影视图父视图。");
                        view=drawing.CreateUnfoldedViewAt3(cell.X+cell.W/2,cell.Y+cell.H/2,0,false);
                    }
                    else view=drawing.CreateDrawViewFromModelView3(full,i==0?StandardView(model,report.Recognition.PrimaryView):ViewName(model,"*Isometric","*等轴测"),cell.X+cell.W/2,cell.Y+cell.H/2,0);
                    if(view==null) throw new InvalidOperationException("创建视图失败，序号："+i);
                    view.ReferencedConfiguration=config;
                    if(view.ReferencedConfiguration!=config) throw new InvalidOperationException("视图配置回读失败。");
                    view.UseSheetScale=0; view.ScaleDecimal=1;
                    Rebuild(doc);
                    double[] b=(double[])view.GetOutline();
                    if(b==null || b[2]<=b[0] || b[3]<=b[1]) throw new InvalidOperationException("视图为空或边界无效。");
                    bounds.Add(Tuple.Create(b[2]-b[0],b[3]-b[1],cell));
                    created.Add(view);
                }
                double scale=LayoutPlanner.Fit(report.Rack!=null?bounds.Take(3):bounds);
                if(options.FixedScale>0)
                {
                    if((report.Rack!=null?bounds.Take(3):bounds).Any(b=>b.Item1*options.FixedScale>b.Item3.W-.012 || b.Item2*options.FixedScale>b.Item3.H-.012)) throw new InvalidOperationException("指定比例不能容纳视图，请减小 FixedScale 或使用更大图幅。");
                    scale=options.FixedScale;
                }
                if(!sheet.SetScale(report.Rack!=null && scale<1?1:scale,report.Rack!=null && scale<1?1/scale:1,false,false)) throw new InvalidOperationException("图纸比例设置失败。");
                for(int i=0;i<created.Count;i++)
                {
                    var view=created[i]; view.UseSheetScale=1;
                    if(report.Rack!=null && i==3) { view.UseSheetScale=0; view.ScaleDecimal=Math.Min(scale,LayoutPlanner.Fit(new[]{bounds[i]})); report.Rack.IsometricScale=view.ScaleDecimal; }
                    Rebuild(doc);
                    double[] b=(double[])view.GetOutline(); var pos=(double[])view.Position; var cell=cells[i];
                    view.Position=new[]{pos[0]+cell.X+cell.W/2-(b[0]+b[2])/2,pos[1]+cell.Y+cell.H/2-(b[1]+b[3])/2};
                }
                stage("导入尺寸与自动标注");
                if(options.ImportDimensions)
                {
                    int types=(int)swInsertAnnotation_e.swInsertDimensions | (int)swInsertAnnotation_e.swInsertCThreads | (int)swInsertAnnotation_e.swInsertholeCallout;
                    if(report.Gear!=null) types &= ~(int)swInsertAnnotation_e.swInsertDimensions;
                    if(report.Rack!=null) types=(int)swInsertAnnotation_e.swInsertCThreads;
                    for(int i=0;i<3;i++)
                    {
                        doc.ClearSelection2(true);
                        if(!drawing.ActivateView(created[i].GetName2())) throw new InvalidOperationException("无法激活标注视图。");
                        if(!doc.Extension.SelectByID2(created[i].GetName2(),"DRAWINGVIEW",0,0,0,false,0,null,0)) throw new InvalidOperationException("无法选择标注视图。");
                        drawing.InsertModelAnnotations3(0,types,false,true,false,false);
                    }
                }
                var beforeAuto=new HashSet<string>(ReadDimensions(drawing).Select(d=>d.Evidence.View+"|"+d.Evidence.FullName));
                if(options.AutoDimension && report.Rack!=null) RackDrawing.Dimensions(doc,created,report,progress);
                if(options.AutoDimension && report.Gear!=null) GearDrawing.Dimensions(doc,created,report);
                if(options.AutoDimension && report.Gear==null && report.Rack==null)
                foreach(var view in created.Take(3))
                {
                    doc.ClearSelection2(true);
                    drawing.ActivateView(view.GetName2());
                    if(!doc.Extension.SelectByID2(view.GetName2(),"DRAWINGVIEW",0,0,0,false,0,null,0)) throw new InvalidOperationException("无法选择自动标注视图。");
                    int before=DimensionCount(view),attempts=1;
                    int status=drawing.AutoDimension(1,options.Ordinate?2:1,-1,options.Ordinate?2:1,1);
                    int count=DimensionCount(view);
                    // Retry only if NOTHING was inserted. Never retry a partial success and multiply dimensions.
                    if(count==before && status!=0)
                    {
                        doc.ClearSelection2(true); doc.Extension.SelectByID2(view.GetName2(),"DRAWINGVIEW",0,0,0,false,0,null,0);
                        status=drawing.AutoDimension(0,options.Ordinate?2:1,1,options.Ordinate?2:1,1); attempts++; count=DimensionCount(view);
                    }
                    report.AutoDimensions.Add(new AutoDimensionEvidence { View=view.GetName2(),ReturnCode=status,Before=before,After=count,Attempts=attempts });
                    if(status!=(int)swAutodimStatus_e.swAutodimStatusSuccess)
                        report.Warnings.Add("自动标注 "+view.GetName2()+" 返回 "+status+"，实测新增 "+(count-before)+" 个尺寸；保留返回值异常并要求复核。");
                }
                var generated=new HashSet<string>(ReadDimensions(drawing).Select(d=>d.Evidence.View+"|"+d.Evidence.FullName).Where(k=>!beforeAuto.Contains(k)));
                stage("倒角直接标注与尺寸排列");
                if(options.Deduplicate) Deduplicate(doc,drawing,report);
                if(options.DirectChamfers && report.Gear==null && report.Rack==null) ChamferAnnotations.InsertAndReplace(app,doc,created,generated,report,progress);
                if(options.DirectFillets && report.Gear==null && report.Rack==null) { stage("圆角识别与标注"); FilletAnnotations.Insert(doc,created,generated,report,progress); }
                if(options.Deduplicate) { stage("重复标注精简"); DimensionReduction.Reduce(doc,created,generated,report,progress); }
                foreach(var group in ReadDimensions(drawing).Where(d=>d.Evidence.Type!=10 && report.Rack==null).GroupBy(d=>d.Evidence.View))
                {
                    doc.ClearSelection2(true);
                    int n=0;
                    foreach(var item in group) if(item.Annotation.Select3(n>0,null)) n++;
                    progress("SolidWorks 原生尺寸排列："+group.Key+"，"+n+" 项");
                    if(n>1 && !doc.Extension.AlignDimensions(0,options.SpacingMm/1000)) report.Warnings.Add("尺寸自动排列失败："+group.Key);
                }
                // Keep native associations and native dimension lanes. A view's cell bounds only its geometry;
                // clipping every dimension to that cell collapses several lanes onto one point on small sheets.
                // Bound text by the printable area/title-block reserve instead of the view geometry cell.
                foreach(var item in ReadDimensions(drawing))
                {
                    int vi=created.FindIndex(v=>v.GetName2()==item.Evidence.View); if(vi<0) continue;
                    var p=item.Evidence.Position;
                    double x=Math.Max(options.LeftMarginMm/1000+.008,Math.Min(width-options.RightMarginMm/1000-.008,p[0]));
                    double y=Math.Max(options.TitleBlockHeightMm/1000+.008,Math.Min(height-options.TopMarginMm/1000-.008,p[1]));
                    if(Math.Abs(x-p[0])>1e-7 || Math.Abs(y-p[1])>1e-7)
                    {
                        if(!item.Annotation.SetPosition2(x,y,p.Length>2?p[2]:0)) report.Warnings.Add("图幅外尺寸重定位失败："+item.Evidence.FullName);
                        else report.RepositionedDimensions++;
                    }
                }
                if(options.OptimizeAnnotationLayout) {
                    stage("标注线分层与几何避让");
                    if(report.Rack==null) AnnotationLayout.Arrange(doc,created,report,progress);
                    else { AnnotationLayout.ApplyStyle(doc,report); foreach(var item in ReadDimensions(drawing)) { item.Annotation.Layer="AUTO_DIMENSIONS"; item.Annotation.LayerOverride=0; } }
                }
                if(report.Gear!=null) { stage("齿轮参数表"); GearDrawing.InsertTable(doc,gearTableArea,report); }
                if(report.Rack!=null) { stage("齿条参数及安装孔表"); RackDrawing.Tables(doc,options,report); }
                doc.ClearSelection2(true); drawing.ActivateView(""); Rebuild(doc);
                ChamferAnnotations.Verify(drawing,report,false);
                FilletAnnotations.Verify(drawing,report,false);
                var after=ReadDimensions(drawing);
                if(options.Deduplicate && after.Where(d=>DimensionRules.Key(d.Evidence)!=null).GroupBy(d=>DimensionRules.Key(d.Evidence)).Any(g=>g.Count()>1))
                    report.Warnings.Add("重建后仍存在同源重复尺寸。");
                report.Dimensions=after.Select(d=>d.Evidence).ToList();
                if(after.Any(d=>!d.Evidence.ValueVerified)) report.Warnings.Add("存在无法可靠回读的已有倒角尺寸，已保留文字和关联、要求人工复核；未将其作为数值验证或去重依据。");
                if(after.Count==0) report.Warnings.Add("没有生成尺寸：导入模型可能没有参数尺寸，请启用自动标注或人工补标。");
                if(after.Any(d=>d.Evidence.Dangling)) report.Warnings.Add("存在悬空尺寸，保留并要求人工修复。");
                for(int i=0;i<created.Count;i++) if(!cells[i].Contains((double[])created[i].GetOutline(),0)) report.Warnings.Add("视图超出布局单元："+created[i].GetName2());
                stage("保存与中心标记");
                if(report.Rack!=null && options.OptimizeAnnotationLayout) AnnotationLayout.StyleNotes(created);
                Save(doc,target);
                ThreadAnnotations.Insert(doc,created,cells,report);
                if(options.OptimizeAnnotationLayout) AnnotationLayout.StyleNotes(created);
                if(report.ThreadCallouts.Count!=report.Recognition.Threads.Count)
                    throw new InvalidOperationException("识别到的螺纹尚未全部建立可追溯标注，已停止导出 PDF/DXF。请查看 Threads 和 Warnings。");
                if(report.ThreadCallouts.Count>0) { Rebuild(doc); Save(doc,target); }
                if(options.CenterMarks)
                {
                    foreach(var view in created.Take(3))
                    {
                        drawing.ActivateView(view.GetName2());
                        if(!view.AutoInsertCenterMarks2(1,0,true,true,true,0,0,false,true,0)) report.Warnings.Add("中心标记插入返回失败："+view.GetName2());
                    }
                    Rebuild(doc); report.CenterMarkCount=Marks(created.Take(3));
                    if(report.Recognition.Geometry.Any(g=>g.Kind=="hole") && report.CenterMarkCount==0) report.Warnings.Add("识别到孔但未回读到中心标记。");
                    drawing.ActivateView(""); Save(doc,target);
                }
                if(options.ExportPdf) { Save(doc,Path.ChangeExtension(target,".pdf")); report.Pdf=Path.ChangeExtension(target,".pdf"); }
                if(options.ExportDxf) { Save(doc,Path.ChangeExtension(target,".dxf")); report.Dxf=Path.ChangeExtension(target,".dxf"); }
                stage("保存重开校验");
                // Close and reopen our own document to verify persisted annotations and view count.
                app.CloseDoc(doc.GetTitle()); doc=null;
                int re=0,rw=0;
                doc=app.OpenDoc6(target,3,1,"",ref re,ref rw);
                if(doc==null || re!=0 || rw!=0) throw new IOException("工程图重开失败/警告："+re+"/"+rw);
                var reopened=(DrawingDoc)doc;
                var persisted=ReadDimensions(reopened).Select(d=>d.Evidence).ToList();
                if(Views(reopened).Count()!=created.Count || !persisted.Select(SavedSignature).OrderBy(x=>x).SequenceEqual(report.Dimensions.Select(SavedSignature).OrderBy(x=>x))) throw new IOException("保存重开后视图或尺寸数值/类型/配置不一致。");
                var persistedProps=(double[])((Sheet)reopened.GetCurrentSheet()).GetProperties();
                if((persistedProps[4]!=0)!=options.FirstAngle || Math.Abs(persistedProps[2]/persistedProps[3]-scale)>1e-9) throw new IOException("保存重开后投影法或比例不一致。");
                var savedSheet=(Sheet)reopened.GetCurrentSheet(); savedSheet.GetSize(ref readWidth,ref readHeight);
                if(Math.Abs(readWidth-width)>1e-8 || Math.Abs(readHeight-height)>1e-8) throw new IOException("保存重开后图幅不一致。");
                if(!string.Equals(savedSheet.GetTemplateName(),report.SheetFormat,StringComparison.OrdinalIgnoreCase)) throw new IOException("保存重开后图纸格式不一致。");
                report.SheetInitialization.EmbeddedSegmentsReopened=SheetSetup.Segments(savedSheet);
                if(report.SheetInitialization.EmbeddedSegmentsReopened!=report.SheetInitialization.EmbeddedSegmentsAfter) throw new IOException("保存重开后内嵌图框元素数量不一致。");
                CheckProperties(doc,report.DrawingProperties);
                report.Dimensions=persisted;
                ThreadAnnotations.Verify(reopened,report);
                ChamferAnnotations.Verify(reopened,report,true);
                FilletAnnotations.Verify(reopened,report,true);
                AnnotationLayout.Verify(Views(reopened),report);
                RackDrawing.Verify(reopened,report);
                GearDrawing.Verify(reopened,report);
                if(Marks(Views(reopened))!=report.CenterMarkCount && options.CenterMarks) throw new IOException("保存重开后中心标记数量不一致。");
                ModelRecognizer.Coverage(report.Recognition,persisted);
                if(report.Rack!=null) foreach(var geometry in report.Recognition.Geometry.Where(g=>report.Rack.Holes.Any(h=>h.Entities.Contains(g.Id)))) geometry.SizeCoverage="tabulated_snapshot";
                foreach(var missing in report.Recognition.Geometry.Where(g=>(g.Kind=="hole" || g.Kind=="slot" || g.Kind=="outer_cylinder") && g.SizeCoverage!="linked_size_dimension" && g.SizeCoverage!="tabulated_snapshot"))
                    report.Warnings.Add("未建立尺寸与几何的直接关联："+missing.Kind+" "+missing.Owner+"；需检查规格和定位。");
                report.Warnings.Add("待工程复核：孔槽规格/定位覆盖、关键尺寸完整性、公差、标题栏和引线；识别到特征不代表已完整标注。");
                report.Status="review_required"; completed=true;
            }
            catch(Exception ex) { report.Error=ex.ToString(); }
            finally
            {
                AnnotationLayout.Release(report);
                try
                {
                    if(doc!=null && completed) app.CloseDoc(doc.GetTitle());
                    if(model!=null && originalConfig!=null && model.ConfigurationManager.ActiveConfiguration.Name!=originalConfig && !model.ShowConfiguration2(originalConfig)) report.Warnings.Add("恢复原配置失败："+originalConfig);
                    if(opened && model!=null) app.CloseDoc(model.GetTitle());
                    if(previous!=null) { int e=0; app.ActivateDoc3(previous.GetTitle(),false,0,ref e); }
                }
                catch(Exception ex) { report.Warnings.Add("会话恢复异常："+ex.Message); }
                report.Stages.Add(new StageEvidence { Name=currentStage,ElapsedMs=timer.ElapsedMilliseconds-checkpoint });
                string reportPath=Path.ChangeExtension(target,".report.json"); report.Write(reportPath);
                if(completed && options.AutoReviewPdf) {
                    long started=timer.ElapsedMilliseconds; PdfAudit.Run(reportPath,report);
                    report.Stages.Add(new StageEvidence { Name="PDF 文字审查",ElapsedMs=timer.ElapsedMilliseconds-started }); report.Write(reportPath);
                }
            }
            return report;
        }
    }
}
