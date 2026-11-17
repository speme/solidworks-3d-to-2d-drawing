using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace AutoDrawing
{
    public sealed class TemplateEditorForm : Form
    {
        private readonly DrawingOptions options;
        private readonly TextBox template=new TextBox { Width=500 }, format=new TextBox { Width=500 };
        private readonly PropertyGrid layout=new PropertyGrid { Dock=DockStyle.Fill,ToolbarVisible=false };
        private readonly DataGridView fields=new DataGridView { Dock=DockStyle.Fill,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill,AllowUserToAddRows=true,AllowUserToDeleteRows=true };
        public DrawingOptions Options { get { return options; } }
        public TemplateEditorForm(DrawingOptions source)
        {
            options=new JavaScriptSerializer().Deserialize<DrawingOptions>(new JavaScriptSerializer().Serialize(source));
            Text="自定义模板方案"; Size=new Size(850,700); MinimumSize=new Size(800,600); Font=new Font("Microsoft YaHei UI",9);
            var root=new TableLayoutPanel { Dock=DockStyle.Fill,RowCount=5,ColumnCount=1,Padding=new Padding(10) };
            root.RowStyles.Add(new RowStyle(SizeType.Absolute,40)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,40)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,50)); root.RowStyles.Add(new RowStyle(SizeType.Percent,100)); root.RowStyles.Add(new RowStyle(SizeType.Absolute,45));
            root.Controls.Add(PathRow("工程图 .drwdot",template,"*.drwdot")); root.Controls.Add(PathRow("图纸格式 .slddrt",format,"*.slddrt"));
            root.Controls.Add(new Label { Dock=DockStyle.Fill,Text="图框/Logo/标题栏版式在 SolidWorks 中编辑。此处配置布局和标题栏属性。\n标题栏文字应链接 $PRP:\"属性名\"；可用 {ModelName}、{Configuration}、{Date}。" });
            var split=new SplitContainer { Dock=DockStyle.Fill,Size=new Size(800,430),SplitterDistance=420,Panel1MinSize=300,Panel2MinSize=280 };
            Shown+=(s,e)=> { split.SplitterDistance=(int)(split.Width*.55); };
            split.Panel1.Controls.Add(layout); fields.Columns.Add("Name","属性名称"); fields.Columns.Add("Value","属性值"); split.Panel2.Controls.Add(fields); root.Controls.Add(split);
            var actions=new FlowLayoutPanel { Dock=DockStyle.Fill };
            actions.Controls.Add(ActionButton("载入方案…",LoadProfile)); actions.Controls.Add(ActionButton("保存方案…",SaveProfile));
            actions.Controls.Add(ActionButton("应用",()=> { try { Collect(); options.Validate(); DialogResult=DialogResult.OK; Close(); } catch(Exception ex) { MessageBox.Show(this,ex.Message,"模板检查"); } }));
            actions.Controls.Add(ActionButton("取消",()=> { DialogResult=DialogResult.Cancel; Close(); })); root.Controls.Add(actions); Controls.Add(root); Display();
        }
        private static Button ActionButton(string text,Action action)
        { var b=new Button { Text=text,AutoSize=true }; b.Click+=(s,e)=>action(); return b; }
        private FlowLayoutPanel PathRow(string label,TextBox box,string filter)
        {
            var row=new FlowLayoutPanel { Dock=DockStyle.Fill }; row.Controls.Add(new Label { Text=label,Width=125,Margin=new Padding(2,7,2,2) }); row.Controls.Add(box);
            row.Controls.Add(ActionButton("选择…",()=> { using(var d=new OpenFileDialog { Filter=filter+"|"+filter }) if(d.ShowDialog()==DialogResult.OK) box.Text=d.FileName; })); return row;
        }
        private void Display()
        {
            template.Text=options.Template; format.Text=options.SheetFormat;
            layout.SelectedObject=new LayoutSettings(options); fields.Rows.Clear();
            foreach(var pair in options.DrawingProperties) fields.Rows.Add(pair.Key,pair.Value);
        }
        private void Collect()
        {
            layout.Refresh(); Validate(); fields.EndEdit(); options.Template=template.Text; options.SheetFormat=format.Text;
            options.DrawingProperties.Clear();
            foreach(DataGridViewRow row in fields.Rows)
            {
                if(row.IsNewRow) continue; string key=Convert.ToString(row.Cells[0].Value),value=Convert.ToString(row.Cells[1].Value);
                if(string.IsNullOrWhiteSpace(key)) { if(value!="") throw new ArgumentException("属性名称不能为空。"); continue; }
                if(options.DrawingProperties.ContainsKey(key)) throw new ArgumentException("重复的属性名称："+key);
                options.DrawingProperties.Add(key,value);
            }
        }
        private void LoadProfile()
        {
            using(var dialog=new OpenFileDialog { Filter="模板方案 JSON|*.json" })
            if(dialog.ShowDialog()==DialogResult.OK) try
            {
                var profile=TemplateProfiles.Load(dialog.FileName);
                var current=TemplateProfiles.Json().Deserialize<System.Collections.Generic.Dictionary<string,object>>(TemplateProfiles.Json().Serialize(options));
                TemplateProfiles.Merge(current,profile.Options); var loaded=TemplateProfiles.Json().ConvertToType<DrawingOptions>(current);
                foreach(var property in typeof(DrawingOptions).GetProperties()) property.SetValue(options,property.GetValue(loaded,null),null);
                Display();
            } catch(Exception ex) { MessageBox.Show(this,ex.Message,"读取模板方案失败"); }
        }
        private void SaveProfile()
        {
            try
            {
                Collect(); options.Validate();
                using(var dialog=new SaveFileDialog { Filter="模板方案 JSON|*.json",FileName="company-a3.json" })
                if(dialog.ShowDialog()==DialogResult.OK) TemplateProfiles.Save(dialog.FileName,Path.GetFileNameWithoutExtension(dialog.FileName),options);
            } catch(Exception ex) { MessageBox.Show(this,ex.Message,"保存模板方案失败"); }
        }
        private sealed class LayoutSettings
        {
            private readonly DrawingOptions o;
            public LayoutSettings(DrawingOptions o) { this.o=o; }
            [Category("1. 图幅"),DisplayName("宽度 mm（0=模板）")] public double Width { get { return o.PaperWidthMm; } set { o.PaperWidthMm=value; } }
            [Category("1. 图幅"),DisplayName("高度 mm（0=模板）")] public double Height { get { return o.PaperHeightMm; } set { o.PaperHeightMm=value; } }
            [Category("1. 图幅"),DisplayName("第一角投影")] public bool FirstAngle { get { return o.FirstAngle; } set { o.FirstAngle=value; } }
            [Category("2. 布局"),DisplayName("固定比例（0=自动，0.5=1:2）")] public double Scale { get { return o.FixedScale; } set { o.FixedScale=value; } }
            [Category("2. 布局"),DisplayName("左边距 mm")] public double Left { get { return o.LeftMarginMm; } set { o.LeftMarginMm=value; } }
            [Category("2. 布局"),DisplayName("右边距 mm")] public double Right { get { return o.RightMarginMm; } set { o.RightMarginMm=value; } }
            [Category("2. 布局"),DisplayName("上边距 mm")] public double Top { get { return o.TopMarginMm; } set { o.TopMarginMm=value; } }
            [Category("2. 布局"),DisplayName("底部标题栏预留 mm")] public double Title { get { return o.TitleBlockHeightMm; } set { o.TitleBlockHeightMm=value; } }
            [Category("2. 布局"),DisplayName("视图间距 mm")] public double Gap { get { return o.ViewGapMm; } set { o.ViewGapMm=value; } }
            [Category("3. 视图标注"),DisplayName("轴测图")] public bool Isometric { get { return o.Isometric; } set { o.Isometric=value; } }
            [Category("3. 视图标注"),DisplayName("自动主视图")] public bool AutoOrient { get { return o.AutoOrient; } set { o.AutoOrient=value; } }
            [Category("3. 视图标注"),DisplayName("标注间距 mm")] public double Spacing { get { return o.SpacingMm; } set { o.SpacingMm=value; } }
        }
    }
}
