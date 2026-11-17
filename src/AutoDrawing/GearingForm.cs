using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows.Forms;
using System.Web.Script.Serialization;

namespace AutoDrawing
{
    public sealed class GearingForm : Form
    {
        public GearingInput Input { get; private set; }
        private readonly ComboBox family=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Width=145 },basis=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Width=110 },positive=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Width=100 };
        private readonly DataGridView values=new DataGridView { Dock=DockStyle.Fill,AllowUserToAddRows=false,AllowUserToDeleteRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill };
        private readonly TextBox output=new TextBox { Dock=DockStyle.Fill,ReadOnly=true,Multiline=true,ScrollBars=ScrollBars.Both };
        private readonly CheckBox standard=new CheckBox { Text="原图明确：标准全齿高、无变位、未修齿顶",AutoSize=true };
        private readonly TextBox datum=new TextBox { Width=160 },view=new TextBox { Width=160 },feature=new TextBox { Width=145 };
        private readonly TextBox tolerance=new TextBox { Width=65,Text="1e-6" };
        private readonly ComboBox hand=new ComboBox { DropDownStyle=ComboBoxStyle.DropDownList,Width=100 };
        public static string Label(string key)
        {
            var labels=new Dictionary<string,string> { {"ModuleMm","直齿/大端模数 m"},{"Teeth","齿数 z1"},{"MateTeeth","配对齿数 z2"},{"PitchDiameterMm","分度圆 d（锥齿大端）"},{"MatePitchDiameterMm","配对分度圆 d2"},{"BaseDiameterMm","基圆直径 db"},{"OutsideDiameterMm","外齿齿顶外径 da"},{"TipDiameterMm","内齿齿顶内径 da"},{"RootDiameterMm","齿根圆直径 df"},{"CircularPitchMm","端面/直齿圆周节距 p"},{"BasePitchMm","基节 pb"},{"PressureAngleDeg","直齿压力角 α"},{"NormalPressureAngleDeg","法向压力角 αn"},{"TransversePressureAngleDeg","端面压力角 αt"},{"NormalModuleMm","法向模数 mn"},{"TransverseModuleMm","端面模数 mt"},{"HelixAngleDeg","螺旋角 β"},{"HelixLeadMm","螺旋线单圈导程"},{"ShaftAngleDeg","轴交角 Σ"},{"ConeAngleDeg","大端分度锥角 δ1"},{"MateConeAngleDeg","配对锥角 δ2"},{"ConeDistanceMm","锥距 R"},{"FaceWidthMm","齿宽 b"},{"MeanModuleMm","中点模数"},{"WormStarts","蜗杆头数 z1"},{"WheelTeeth","蜗轮齿数 z2"},{"WormPitchDiameterMm","蜗杆分度圆 d1"},{"WheelPitchDiameterMm","蜗轮分度圆 d2"},{"AxialModuleMm","蜗杆轴向模数 mx"},{"AxialPitchMm","蜗杆轴向齿距 px"},{"LeadMm","蜗杆导程 L"},{"LeadAngleDeg","蜗杆导程角 γ"},{"AxialPressureAngleDeg","蜗杆轴向压力角 αx"},{"AxialDiameterQuotient","轴向直径系数 qx=d1/mx"},{"CenterDistanceMm","中心距 a"},{"Ratio","传动比 i"},{"RackFlankSlope","直齿条齿侧法向比 |nh/nl|"},{"FirstToothCenterFromStartMm","起端到第1齿中心线距离"},{"ReferenceAngleDeg","零位基准观测角"},{"FeatureAngleDeg","指定起始特征观测角"},{"ProfileShift","变位系数 x"},{"StartAngleDeg","指定起始角 θ0"},{"PhaseWithinPitchDeg","一个齿距内的相位角"},{"RackPhaseMm","一个齿距内的线性相位"},{"LinearPitchPhaseDeg","线性齿距等效相位（非旋转角）"} };
            foreach(var pair in new[]{Tuple.Create("Center","轮心"),Tuple.Create("Datum","基准点"),Tuple.Create("Feature","起始特征点")}) { labels[pair.Item1+"Xmm"]=pair.Item2+" X（观察平面）"; labels[pair.Item1+"Ymm"]=pair.Item2+" Y（观察平面）"; }
            labels["MateBaseDiameterMm"]="配对基圆直径 db2"; labels["OperatingPressureAngleDeg"]="工作啮合角 αw（非基准压力角）";
            string label; return labels.TryGetValue(key,out label)?label:key;
        }
        public GearingForm(GearingInput current)
        {
            Text="原图齿轮公式与参数推算"; Width=1050; Height=820; Font=new System.Drawing.Font("Microsoft YaHei UI",9);
            var panel=new TableLayoutPanel { Dock=DockStyle.Fill,RowCount=6,ColumnCount=1,Padding=new Padding(10) };
            panel.RowStyles.Add(new RowStyle(SizeType.Absolute,45)); panel.RowStyles.Add(new RowStyle(SizeType.Percent,58)); panel.RowStyles.Add(new RowStyle(SizeType.Absolute,36)); panel.RowStyles.Add(new RowStyle(SizeType.Absolute,70)); panel.RowStyles.Add(new RowStyle(SizeType.Percent,42)); panel.RowStyles.Add(new RowStyle(SizeType.Absolute,40));
            var top=new FlowLayoutPanel { Dock=DockStyle.Fill };
            family.Items.AddRange(new object[]{"直齿条","外直齿轮","内直齿轮","外斜齿轮","内斜齿轮","直锥齿轮副","正交圆柱蜗杆副"});
            basis.Items.AddRange(new object[]{"基面未指定","法向 normal","端面 transverse","轴向 axial"});
            top.Controls.Add(new System.Windows.Forms.Label { Text="齿制",AutoSize=true }); top.Controls.Add(family); top.Controls.Add(basis);
            var calculate=new Button { Text="计算并检查约束",AutoSize=true }; top.Controls.Add(calculate); top.Controls.Add(new System.Windows.Forms.Label { Text="一致性相对阈值",AutoSize=true }); top.Controls.Add(tolerance);
            top.Controls.Add(new System.Windows.Forms.Label { Text="尺寸用 mm、角度用 °；仅填写原图已知量，空项留空",AutoSize=true }); panel.Controls.Add(top);
            values.Columns.Add(new DataGridViewTextBoxColumn { Name="Quantity",HeaderText="原图特性",ReadOnly=true }); values.Columns.Add("Value","已知数值"); values.Columns.Add("Source","来源（图号/尺寸标记）");
            foreach(string key in GearingCalculator.Quantities) { double n; string source; values.Rows.Add(Label(key),current!=null && current.Measurements.TryGetValue(key,out n)?n.ToString("G12",CultureInfo.InvariantCulture):"",current!=null && current.Evidence.TryGetValue(key,out source)?source:""); values.Rows[values.Rows.Count-1].Tag=key; }
            panel.Controls.Add(values); panel.Controls.Add(standard);
            var angles=new FlowLayoutPanel { Dock=DockStyle.Fill };
            positive.Items.AddRange(new object[]{"正向未指定","CCW 逆时针","CW 顺时针"});
            foreach(var pair in new[]{Tuple.Create("零位基准",datum),Tuple.Create("观察方向",view),Tuple.Create("指定起始特征",feature)}) { angles.Controls.Add(new System.Windows.Forms.Label { Text=pair.Item1,AutoSize=true }); angles.Controls.Add(pair.Item2); }
            hand.Items.AddRange(new object[]{"旋向未指定","右旋 RH","左旋 LH"}); hand.SelectedIndex=current==null?0:current.Hand=="RH"?1:current.Hand=="LH"?2:0;
            angles.Controls.Add(positive); angles.Controls.Add(hand); angles.Controls.Add(new System.Windows.Forms.Label { Text="角度按同一观察方向/正向输入；坐标按观察平面 X向右、Y向上。齿侧/键槽等基准须明确。",AutoSize=true }); panel.Controls.Add(angles); panel.Controls.Add(output);
            family.SelectedIndex=current==null?0:Array.IndexOf(GearingCalculator.Families,current.Family); basis.SelectedIndex=current==null?0:current.ModuleSystem=="normal"?1:current.ModuleSystem=="transverse"?2:current.ModuleSystem=="axial"?3:0;
            positive.SelectedIndex=current==null?0:current.PositiveRotation=="CCW"?1:current.PositiveRotation=="CW"?2:0;
            if(current!=null) { standard.Checked=current.StandardUnshiftedFullDepth; datum.Text=current.PhaseDatum; view.Text=current.ViewingDirection; feature.Text=current.StartFeature; tolerance.Text=current.RelativeTolerance.ToString("G",CultureInfo.InvariantCulture); }
            calculate.Click+=(s,e)=> { try { ShowResult(Build()); } catch(Exception ex) { output.Text=ex.Message; } };
            var buttons=new FlowLayoutPanel { Dock=DockStyle.Fill }; var apply=new Button { Text="附加到出图方案",AutoSize=true };
            apply.Click+=(s,e)=> { try { var input=Build(); var result=ShowResult(input); if(result.Status=="conflict") throw new ArgumentException("请先解决尺寸约束冲突。"); if(input.Family=="worm_pair") throw new ArgumentException("蜗杆副目前仅提供公式推算，尚未验证自动制造标注；可保存计算结果。"); Input=input; DialogResult=DialogResult.OK; Close(); } catch(Exception ex) { MessageBox.Show(this,ex.Message,"公式检查"); } };
            var save=new Button { Text="保存计算结果…",AutoSize=true }; save.Click+=(s,e)=> { try { var result=ShowResult(Build()); using(var file=new SaveFileDialog { Filter="计算报告|*.json" }) if(file.ShowDialog(this)==DialogResult.OK) System.IO.File.WriteAllText(file.FileName,new JavaScriptSerializer().Serialize(result)); } catch(Exception ex) { output.Text=ex.Message; } };
            var clear=new Button { Text="清除附加计算",AutoSize=true }; clear.Click+=(s,e)=> { Input=null; DialogResult=DialogResult.OK; Close(); };
            buttons.Controls.Add(apply); buttons.Controls.Add(save); buttons.Controls.Add(clear); buttons.Controls.Add(new Button { Text="取消",DialogResult=DialogResult.Cancel }); panel.Controls.Add(buttons); Controls.Add(panel);
        }
        private GearingInput Build()
        {
            values.EndEdit(); var input=new GearingInput { Family=GearingCalculator.Families[family.SelectedIndex],ModuleSystem=new[]{"","normal","transverse","axial"}[basis.SelectedIndex],StandardUnshiftedFullDepth=standard.Checked,PhaseDatum=datum.Text.Trim(),ViewingDirection=view.Text.Trim(),StartFeature=feature.Text.Trim(),PositiveRotation=new[]{"","CCW","CW"}[positive.SelectedIndex],Hand=new[]{"","RH","LH"}[hand.SelectedIndex] };
            double threshold; if(!double.TryParse(tolerance.Text,NumberStyles.Float,CultureInfo.InvariantCulture,out threshold)) throw new ArgumentException("数值比较阈值无效。"); input.RelativeTolerance=threshold;
            foreach(DataGridViewRow row in values.Rows) { string raw=Convert.ToString(row.Cells[1].Value).Trim(); if(raw=="") continue; double n; if(!double.TryParse(raw,NumberStyles.Float,CultureInfo.InvariantCulture,out n)) throw new ArgumentException("数值无效："+row.Cells[0].Value); string key=(string)row.Tag; input.Measurements[key]=n; input.Evidence[key]=Convert.ToString(row.Cells[2].Value); }
            return input;
        }
        private GearingResult ShowResult(GearingInput input)
        {
            var result=GearingCalculator.Calculate(input); var lines=new List<string> { "状态："+result.Status+"（仅几何/代数推算，仍需原图和制造复核）" };
            foreach(var p in result.Values) { var step=result.Derivations.FirstOrDefault(d=>d.Quantity==p.Key); lines.Add(Label(p.Key)+" = "+p.Value.ToString("G12",CultureInfo.InvariantCulture)+"    "+(step==null?"原图输入":step.Formula)); }
            lines.AddRange(result.Issues); if(result.Missing.Count>0) lines.Add("尚缺："+string.Join("、",result.Missing.Select(Label))); output.Text=string.Join(Environment.NewLine,lines); return result;
        }
    }
}
