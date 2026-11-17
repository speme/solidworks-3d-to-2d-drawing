using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using SolidWorks.Interop.sldworks;
using Environment = System.Environment;

namespace AutoDrawing
{
    public sealed class DrawingForm : Form
    {
        private readonly SldWorks app;
        private readonly ListBox files=new ListBox { Dock=DockStyle.Fill,SelectionMode=SelectionMode.MultiExtended };
        private readonly TextBox template=new TextBox { Width=400 }, output=new TextBox { Width=400 }, config=new TextBox { Width=160 };
        private readonly TextBox log=new TextBox { Multiline=true,ReadOnly=true,Dock=DockStyle.Fill,ScrollBars=ScrollBars.Vertical };
        private readonly CheckBox iso=Check("轴测图",true), import=Check("导入模型尺寸/孔标注",true), auto=Check("几何自动标注",true), ordinate=Check("坐标尺寸（否则基线）",false), dedup=Check("重复标注精简",true), chamfer=Check("倒角直接标注",true), directRadius=Check("圆角 R 标注",true), lineLayout=Check("标注线避让",true), dxf=Check("同时导出 DXF",true);
        private readonly CheckBox orient=Check("自动主视图",true), allConfigs=Check("全部配置",false), marks=Check("孔中心标记",true), pdfAudit=Check("PDF 文字审查",true);
        private readonly NumericUpDown titleHeight=new NumericUpDown { Minimum=35,Maximum=150,Value=65,Width=60 };
        private readonly ProgressBar progressBar=new ProgressBar { Width=200 };
        private readonly List<string> failed=new List<string>();
        private readonly Button retry=new Button { Text="重试失败件",AutoSize=true,Enabled=false };
        private readonly ComboBox projection=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Width=180 };
        private readonly NumericUpDown spacing=new NumericUpDown { Minimum=4,Maximum=25,Value=8,Width=60 };
        private readonly Button run=new Button { Text="开始批量出图",AutoSize=true }, cancel=new Button { Text="停止后续任务",AutoSize=true,Enabled=false };
        private readonly List<Control> mutable=new List<Control>();
        private bool running,stop;
        private DrawingOptions templateOptions=new DrawingOptions();
        private readonly string settings=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"AutoDrawing","settings.json");
        private static CheckBox Check(string text,bool value) { return new CheckBox { Text=text,Checked=value,AutoSize=true }; }
        private static Label Label(string text) { return new Label { Text=text,AutoSize=true,Margin=new Padding(4,9,4,4) }; }
        private Button Button(string text,Action action)
        { var b=new Button { Text=text,AutoSize=true }; b.Click+=(s,e)=>action(); mutable.Add(b); return b; }
        public DrawingForm(SldWorks app)
        {
            this.app=app; Text="AutoDrawing "+RuntimeInfo.Version+" · 自定义模板与自动出图"; Size=new Size(1020,850); MinimumSize=new Size(960,760); Font=new Font("Microsoft YaHei UI",9);
            var grid=new TableLayoutPanel { Dock=DockStyle.Fill,ColumnCount=1,RowCount=7,Padding=new Padding(12) };
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute,80)); grid.RowStyles.Add(new RowStyle(SizeType.Percent,45));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute,46)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute,46));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute,175)); grid.RowStyles.Add(new RowStyle(SizeType.Absolute,70)); grid.RowStyles.Add(new RowStyle(SizeType.Percent,55));
            var actions=new FlowLayoutPanel { Dock=DockStyle.Fill };
            actions.Controls.Add(Button("添加模型…",()=> { using(var d=new OpenFileDialog { Filter="SolidWorks 模型|*.sldprt;*.sldasm",Multiselect=true }) if(d.ShowDialog()==DialogResult.OK) Add(d.FileNames); }));
            actions.Controls.Add(Button("当前文档",()=> {
                var doc=app.ActiveDoc as ModelDoc2;
                if(doc==null) { Log("当前没有活动文档，请先打开零件或装配体。"); return; }
                if(doc.GetType()!=1 && doc.GetType()!=2) { Log("当前是工程图，请切换到需要出图的零件或装配体。"); return; }
                if(string.IsNullOrWhiteSpace(doc.GetPathName())) { Log("当前模型尚未保存："+doc.GetTitle()+"，请先保存模型。"); return; }
                Add(new[]{doc.GetPathName()});
            }));
            actions.Controls.Add(Button("已打开模型",()=> { var docs=app.GetDocuments() as object[]; if(docs!=null) Add(docs.Cast<ModelDoc2>().Where(d=>d.GetType()==1 || d.GetType()==2).Select(d=>d.GetPathName())); }));
            actions.Controls.Add(Button("扫描文件夹…",()=> { using(var d=new FolderBrowserDialog()) if(d.ShowDialog()==DialogResult.OK) { Add(BatchPlanner.Discover(d.SelectedPath)); Log("文件夹扫描完成，已忽略锁文件和链接目录。"); } }));
            actions.Controls.Add(Button("装配体组件",()=> {
                var doc=app.ActiveDoc as ModelDoc2;
                if(doc==null || doc.GetType()!=2) { Log("请先激活装配体。"); return; }
                var parts=((AssemblyDoc)doc).GetComponents(false) as object[] ?? new object[0];
                Add(parts.Cast<Component2>().Where(c=>!c.IsSuppressed()).Select(c=>c.GetPathName()));
                Log("已加入未压缩组件的已保存文件；去除重复路径，按所选配置/当前配置出图，虚拟组件须先保存。");
            }));
            actions.Controls.Add(Button("移除选中",()=> { foreach(var x in files.SelectedItems.Cast<string>().ToArray()) files.Items.Remove(x); }));
            actions.Controls.Add(Button("齿轮参数…",()=> { using(var editor=new GearParameterForm(templateOptions.GearParameters)) if(editor.ShowDialog(this)==DialogResult.OK) templateOptions.GearParameters=editor.Parameters; }));
            actions.Controls.Add(Button("齿条参数…",()=> { using(var editor=new GearParameterForm(templateOptions.RackParameters,true)) if(editor.ShowDialog(this)==DialogResult.OK) templateOptions.RackParameters=editor.Parameters; }));
            actions.Controls.Add(Button("齿轮公式…",()=> { using(var editor=new GearingForm(templateOptions.GearInference)) if(editor.ShowDialog(this)==DialogResult.OK) templateOptions.GearInference=editor.Input; }));
            grid.Controls.Add(actions); grid.Controls.Add(files);
            var t=new FlowLayoutPanel { Dock=DockStyle.Fill }; t.Controls.Add(Label("工程图模板")); t.Controls.Add(template);
            t.Controls.Add(Button("选择…",()=> { using(var d=new OpenFileDialog { Filter="工程图模板|*.drwdot" }) if(d.ShowDialog()==DialogResult.OK) template.Text=d.FileName; }));
            t.Controls.Add(Button("自定义模板…",()=> { using(var editor=new TemplateEditorForm(Options())) if(editor.ShowDialog(this)==DialogResult.OK) Apply(editor.Options); })); grid.Controls.Add(t);
            var o=new FlowLayoutPanel { Dock=DockStyle.Fill }; o.Controls.Add(Label("输出文件夹")); o.Controls.Add(output);
            o.Controls.Add(Button("浏览…",()=> { using(var d=new FolderBrowserDialog()) if(d.ShowDialog()==DialogResult.OK) output.Text=d.SelectedPath; })); grid.Controls.Add(o);
            var opts=new FlowLayoutPanel { Dock=DockStyle.Fill }; projection.Items.AddRange(new object[]{"GB/T · 第一角","第三角（自定义模板）"}); projection.SelectedIndex=0;
            opts.Controls.Add(projection); opts.Controls.Add(Label("配置（空=当前）")); opts.Controls.Add(config); opts.Controls.Add(Label("间距 mm")); opts.Controls.Add(spacing);
            foreach(var c in new Control[]{iso,import,auto,ordinate,dedup,chamfer,directRadius,lineLayout,dxf,orient,allConfigs,marks,pdfAudit}) opts.Controls.Add(c);
            var titleGroup=new FlowLayoutPanel { AutoSize=true,AutoSizeMode=AutoSizeMode.GrowAndShrink,WrapContents=false,Margin=new Padding(0) };
            titleGroup.Controls.Add(Label("标题栏预留 mm")); titleGroup.Controls.Add(titleHeight); opts.Controls.Add(titleGroup); grid.Controls.Add(opts);
            mutable.AddRange(new Control[]{files,template,output,config,opts});
            var footer=new FlowLayoutPanel { Dock=DockStyle.Fill }; footer.Controls.Add(run); footer.Controls.Add(cancel); footer.Controls.Add(retry); footer.Controls.Add(progressBar); footer.Controls.Add(Label("输出 PDF、几何识别及批次报告。停止在当前零件完成后生效。")); grid.Controls.Add(footer); grid.Controls.Add(log); Controls.Add(grid);
            run.Click+=(s,e)=>Start(); cancel.Click+=(s,e)=> { stop=true; cancel.Enabled=false; Log("将在当前模型完成后停止。"); };
            retry.Click+=(s,e)=> { var paths=failed.ToArray(); files.Items.Clear(); Add(paths); Start(); };
            FormClosing+=(s,e)=> { if(running) { e.Cancel=true; stop=true; Log("正在出图，当前任务结束后可关闭。"); } };
            output.Text=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),"AutoDrawing");
            try { template.Text=app.GetDocumentTemplate(3,"",0,0,0); } catch { }
            if(File.Exists(settings)) try {
                var json=new JavaScriptSerializer(); string content=File.ReadAllText(settings);
                var saved=json.Deserialize<Dictionary<string,object>>(content); var options=json.Deserialize<DrawingOptions>(content);
                // Migrate the old default once; later explicitly saved choices remain respected.
                if(!saved.ContainsKey("SettingsVersion") || Convert.ToInt32(saved["SettingsVersion"])<2) options.ExportDxf=true;
                if(!saved.ContainsKey("SettingsVersion") || Convert.ToInt32(saved["SettingsVersion"])<3) options.AutoReviewPdf=true;
                Apply(options);
            } catch { Log("配置文件无法读取，使用默认配置。"); }
            Log("运行版本："+RuntimeInfo.Version+"；加载路径："+RuntimeInfo.AssemblyPath);
        }
        private void Add(IEnumerable<string> paths)
        {
            foreach(var path in paths)
            {
                if(string.IsNullOrWhiteSpace(path)) { Log("跳过未保存文档：请先保存模型。"); continue; }
                var full=Path.GetFullPath(path); var ext=Path.GetExtension(full);
                if(ext.Equals(".sldprt",StringComparison.OrdinalIgnoreCase) || ext.Equals(".sldasm",StringComparison.OrdinalIgnoreCase))
                    if(!files.Items.Cast<string>().Contains(full,StringComparer.OrdinalIgnoreCase)) files.Items.Add(full);
            }
        }
        private DrawingOptions Options() { return new DrawingOptions { Template=template.Text,OutputDirectory=output.Text,Configuration=config.Text,FirstAngle=projection.SelectedIndex==0,Isometric=iso.Checked,ImportDimensions=import.Checked,AutoDimension=auto.Checked,Ordinate=ordinate.Checked,Deduplicate=dedup.Checked,DirectChamfers=chamfer.Checked,DirectFillets=directRadius.Checked,OptimizeAnnotationLayout=lineLayout.Checked,GearParameters=new Dictionary<string,string>(templateOptions.GearParameters),RackParameters=new Dictionary<string,string>(templateOptions.RackParameters),GearInference=templateOptions.GearInference,MergeChamfers=templateOptions.MergeChamfers,ExportDxf=dxf.Checked,SpacingMm=(double)spacing.Value,AutoOrient=orient.Checked,AllConfigurations=allConfigs.Checked,CenterMarks=marks.Checked,AutoReviewPdf=pdfAudit.Checked,TitleBlockHeightMm=(double)titleHeight.Value,SheetFormat=templateOptions.SheetFormat,PaperWidthMm=templateOptions.PaperWidthMm,PaperHeightMm=templateOptions.PaperHeightMm,FixedScale=templateOptions.FixedScale,LeftMarginMm=templateOptions.LeftMarginMm,RightMarginMm=templateOptions.RightMarginMm,TopMarginMm=templateOptions.TopMarginMm,ViewGapMm=templateOptions.ViewGapMm,DrawingProperties=new Dictionary<string,string>(templateOptions.DrawingProperties) }; }
        private void Apply(DrawingOptions o) { templateOptions=new JavaScriptSerializer().Deserialize<DrawingOptions>(new JavaScriptSerializer().Serialize(o)); template.Text=o.Template; output.Text=o.OutputDirectory; config.Text=o.Configuration; projection.SelectedIndex=o.FirstAngle?0:1; iso.Checked=o.Isometric; import.Checked=o.ImportDimensions; auto.Checked=o.AutoDimension; ordinate.Checked=o.Ordinate; dedup.Checked=o.Deduplicate; chamfer.Checked=o.DirectChamfers; directRadius.Checked=o.DirectFillets; lineLayout.Checked=o.OptimizeAnnotationLayout; dxf.Checked=o.ExportDxf; spacing.Value=(decimal)Math.Max(4,Math.Min(25,o.SpacingMm)); orient.Checked=o.AutoOrient; allConfigs.Checked=o.AllConfigurations; marks.Checked=o.CenterMarks; pdfAudit.Checked=o.AutoReviewPdf; titleHeight.Value=(decimal)Math.Max(35,Math.Min(150,o.TitleBlockHeightMm)); }
        private void Log(string text) { log.AppendText(DateTime.Now.ToString("HH:mm:ss")+" "+text+Environment.NewLine); if(running) { log.Refresh(); System.Windows.Forms.Application.DoEvents(); } }
        private async void Start()
        {
            if(running) return;
            var options=Options();
            try { options.Validate(); if(files.Items.Count==0) throw new ArgumentException("请添加待出图模型。"); }
            catch(Exception ex) { MessageBox.Show(this,ex.Message,"配置检查"); return; }
            var jobs=files.Items.Cast<string>().ToArray();
            running=true; stop=false; run.Enabled=false; cancel.Enabled=true; mutable.ForEach(c=>c.Enabled=false);
            retry.Enabled=false; failed.Clear();
            var summary=new BatchSummary();
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(settings));
                var settingsJson=new JavaScriptSerializer(); var savedSettings=settingsJson.Deserialize<Dictionary<string,object>>(settingsJson.Serialize(options));
                savedSettings["SettingsVersion"]=3; File.WriteAllText(settings,settingsJson.Serialize(savedSettings));
                var engine=new DrawingEngine(app);
                var expanded=BatchPlanner.Expand(app,jobs,options); summary.Planned=expanded.Count;
                progressBar.Maximum=Math.Max(1,expanded.Count); progressBar.Value=0;
                for(int i=0;i<expanded.Count && !stop;i++)
                {
                    // Yield only BETWEEN jobs; capture WinForms synchronization context. Never Task.Run COM work.
                    await System.Threading.Tasks.Task.Delay(30);
                    if(stop) break;
                    var job=expanded[i]; Log(string.Format("[{0}/{1}] {2}",i+1,expanded.Count,job));
                    try {
                        if(job.PreflightError!=null) throw new InvalidOperationException(job.PreflightError);
                        var r=engine.Run(job.Model,BatchPlanner.ForJob(options,job),Log); summary.Jobs.Add(r);
                        Log(r.Status+" | "+r.Drawing);
                        if(r.Recognition!=null) Log("识别："+r.Recognition.PartClass+"，"+r.Recognition.Geometry.Count+" 项几何，主视图 "+r.Recognition.PrimaryView);
                        foreach(var w in r.Warnings) Log(w); if(r.Error!=null) Log(r.Error);
                        if(r.Status=="failed") failed.Add(job.Model);
                    }
                    catch(Exception ex) { failed.Add(job.Model); summary.Jobs.Add(new JobReport { Model=job.Model,Configuration=job.Configuration,Error=ex.ToString() }); Log("任务失败："+ex.Message); }
                    progressBar.Value=i+1;
                }
                int generated=summary.Jobs.Count(j=>j.Status!="failed"),failures=summary.Jobs.Count(j=>j.Status=="failed");
                Log((stop?"批处理已停止：":"批处理结束：")+"已生成 "+generated+" 件，失败 "+failures+" 件，未执行 "+(expanded.Count-summary.Jobs.Count)+" 件。"+(generated>0?"请复核已生成图纸。":"请查看错误和报告。"));
            }
            catch(Exception ex) { Log(ex.ToString()); }
            finally {
                summary.Cancelled=stop;
                try { Log("批次报告："+summary.Write(options.OutputDirectory)); } catch(Exception ex) { Log("批次报告保存失败："+ex.Message); }
                running=false; run.Enabled=true; cancel.Enabled=false; retry.Enabled=failed.Count>0; mutable.ForEach(c=>c.Enabled=true);
            }
        }
    }
}
