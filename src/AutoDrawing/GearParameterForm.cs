using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
namespace AutoDrawing
{
    public sealed class GearParameterForm : Form
    {
        private readonly DataGridView fields=new DataGridView { Dock=DockStyle.Fill,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill };
        public Dictionary<string,string> Parameters { get; private set; }
        public GearParameterForm(Dictionary<string,string> current):this(current,false) {}
        public GearParameterForm(Dictionary<string,string> current,bool rack)
        {
            Text=(rack?"齿条":"齿轮")+"参数（覆盖模型属性）"; Size=new Size(640,510); Font=new Font("Microsoft YaHei UI",9);
            var panel=new TableLayoutPanel { Dock=DockStyle.Fill,RowCount=3,ColumnCount=1,Padding=new Padding(10) };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute,65)); panel.RowStyles.Add(new RowStyle(SizeType.Percent,100)); panel.RowStyles.Add(new RowStyle(SizeType.Absolute,42));
            panel.Controls.Add(new Label { Dock=DockStyle.Fill,Text="留空时从模型配置/文档属性读取。填写值会覆盖模型参数，并应用到当前批次。\n数值不含单位：模数为 mm，压力角和螺旋角为度。缺失参数显示‘未提供’。" });
            fields.Columns.Add(new DataGridViewTextBoxColumn { Name="Key",HeaderText="参数",ReadOnly=true }); fields.Columns.Add("Value","值");
            string[] labels={"Module · 模数","Teeth · 齿数","PressureAngle · 压力角","HelixAngle · 螺旋角","ProfileShift · 变位系数","GearType · 齿制","Accuracy · 精度等级","HeatTreatment · 热处理","Material · 材料","Hand · 旋向"};
            var keys=rack?RackRules.Keys:GearRules.Keys;
            if(rack) labels=new[]{"Module · 模数","PressureAngle · 压力角","PitchLineFromBack · 分度线距背面 mm","HelixAngle · 螺旋角","Accuracy · 精度等级","Material · 材料","HeatTreatment · 热处理","StartEnd · 起端 Minimum/Maximum"};
            for(int i=0;i<keys.Length;i++) {
                string value; current.TryGetValue(keys[i],out value); fields.Rows.Add(labels[i],value); fields.Rows[i].Tag=keys[i];
                if(keys[i]=="StartEnd") {
                    var choice=new DataGridViewComboBoxCell { DisplayMember="Label",ValueMember="Value",DataSource=new[]{new { Label="默认：模型最小端",Value="" },new { Label="模型长向最小端",Value="Minimum" },new { Label="模型长向最大端",Value="Maximum" }} };
                    fields.Rows[i].Cells[1]=choice; choice.Value=value ?? "";
                }
            }
            panel.Controls.Add(fields); var buttons=new FlowLayoutPanel { Dock=DockStyle.Fill };
            var ok=new Button { Text="应用",AutoSize=true }; ok.Click+=(s,e)=> { try { fields.EndEdit(); var values=new Dictionary<string,string>(); foreach(DataGridViewRow row in fields.Rows) { string value=Convert.ToString(row.Cells[1].Value).Trim(); if(value!="") values.Add((string)row.Tag,value); } if(rack) RackRules.Validate(values); else GearRules.Validate(values); Parameters=values; DialogResult=DialogResult.OK; Close(); } catch(Exception ex) { MessageBox.Show(this,ex.Message,"参数检查"); } };
            var clear=new Button { Text="清空覆盖值",AutoSize=true }; clear.Click+=(s,e)=> { foreach(DataGridViewRow row in fields.Rows) row.Cells[1].Value=""; };
            var cancel=new Button { Text="取消",AutoSize=true,DialogResult=DialogResult.Cancel }; buttons.Controls.Add(ok); buttons.Controls.Add(clear); buttons.Controls.Add(cancel); panel.Controls.Add(buttons); Controls.Add(panel);
        }
    }
}
