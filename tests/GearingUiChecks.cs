using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using AutoDrawing;
class GearingUiChecks
{
    [STAThread] static void Main()
    {
        Application.EnableVisualStyles();
        var input=new GearingInput { Family="worm_pair",ModuleSystem="axial",Hand="RH",Measurements=new Dictionary<string,double> { {"AxialModuleMm",3},{"WormStarts",2},{"WheelTeeth",30},{"WormPitchDiameterMm",44},{"ShaftAngleDeg",90},{"NormalPressureAngleDeg",20} } };
        using(var form=new GearingForm(input)) {
            form.Show(); Application.DoEvents();
            var read=typeof(GearingForm).GetMethod("Build",BindingFlags.Instance|BindingFlags.NonPublic);
            var restored=(GearingInput)read.Invoke(form,null);
            if(restored.Family!=input.Family || restored.Hand!="RH" || restored.Measurements["WormStarts"]!=2 || GearingCalculator.Calculate(restored).Values["Ratio"]!=15) throw new Exception("Formula UI round-trip failed");
            form.Close();
        }
        using(var form=new GearParameterForm(new Dictionary<string,string>{{"StartEnd","Maximum"}},true)) {
            form.Show(); Application.DoEvents();
            var fields=(DataGridView)typeof(GearParameterForm).GetField("fields",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(form);
            bool found=false;
            foreach(DataGridViewRow row in fields.Rows) if((string)row.Tag=="StartEnd") found=row.Cells[1] is DataGridViewComboBoxCell && (string)row.Cells[1].Value=="Maximum";
            if(!found) throw new Exception("Rack end selection UI failed");
            form.Close();
        }
        Console.WriteLine("PASS: formula input and rack start selection UI checks");
    }
}
