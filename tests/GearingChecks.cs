using System;
using System.Collections.Generic;
using System.Linq;
using AutoDrawing;
class GearingChecks
{
    static int count;
    static void Check(bool ok,string name) { count++; if(!ok) throw new Exception(name); }
    static GearingInput Input(string family,params object[] pairs) { var x=new GearingInput { Family=family }; for(int i=0;i<pairs.Length;i+=2) x.Measurements[(string)pairs[i]]=Convert.ToDouble(pairs[i+1]); return x; }
    static bool Near(double a,double b,double tolerance=1e-6) { return Math.Abs(a-b)<tolerance; }
    static void Main()
    {
        var rack=GearingCalculator.Calculate(Input("rack_straight","CircularPitchMm",5*Math.PI,"RackFlankSlope",Math.Tan(20*Math.PI/180)));
        Check(Near(rack.Values["ModuleMm"],5) && Near(rack.Values["PressureAngleDeg"],20),"rack pitch/flank inverse");
        Check(rack.Status=="review_required" && rack.Derivations.Any(d=>d.Formula.Contains("atan")),"formula audit trail");
        var phase=GearingCalculator.Calculate(Input("rack_straight","CircularPitchMm",10,"PressureAngleDeg",20,"FirstToothCenterFromStartMm",23));
        Check(Near(phase.Values["RackPhaseMm"],3) && Near(phase.Values["LinearPitchPhaseDeg"],108),"linear phase is not body rotation");
        var plain=Input("spur_external","OutsideDiameterMm",52,"Teeth",24);
        Check(!GearingCalculator.Calculate(plain).Values.ContainsKey("ModuleMm"),"OD alone cannot assume unshifted standard");
        plain.StandardUnshiftedFullDepth=true; plain.Measurements["BaseDiameterMm"]=48*Math.Cos(20*Math.PI/180);
        var spur=GearingCalculator.Calculate(plain);
        Check(Near(spur.Values["ModuleMm"],2) && Near(spur.Values["PressureAngleDeg"],20),"standard external inverse");
        Check(Near(spur.Values["RootDiameterMm"],43),"standard external root");
        var baseOnly=GearingCalculator.Calculate(Input("spur_external","BaseDiameterMm",48*Math.Cos(20*Math.PI/180),"PressureAngleDeg",20,"Teeth",24));
        Check(Near(baseOnly.Values["ModuleMm"],2),"base circle plus reference pressure resolves pitch diameter");
        Check(spur.Derivations.Where(d=>d.Formula.Contains("da/")).All(d=>d.Condition.Contains("explicit")),"OD rule declares assumption");
        var ring=Input("spur_internal","TipDiameterMm",116,"Teeth",60,"PressureAngleDeg",20); ring.StandardUnshiftedFullDepth=true;
        var internalGear=GearingCalculator.Calculate(ring);
        Check(Near(internalGear.Values["ModuleMm"],2) && Near(internalGear.Values["RootDiameterMm"],125),"internal tip is inner diameter");
        var wrongRing=Input("spur_internal","OutsideDiameterMm",150,"Teeth",60); wrongRing.StandardUnshiftedFullDepth=true;
        Check(!GearingCalculator.Calculate(wrongRing).Values.ContainsKey("ModuleMm"),"ring body outer diameter cannot infer tooth module");
        var helical=Input("helical_external","NormalModuleMm",3,"Teeth",20,"HelixAngleDeg",30,"NormalPressureAngleDeg",20);
        var h=GearingCalculator.Calculate(helical);
        Check(Near(h.Values["PitchDiameterMm"],69.2820323027551),"helical normal module convention");
        Check(Near(h.Values["TransversePressureAngleDeg"],22.7958772588585),"helical pressure conversion");
        Check(Near(h.Values["NormalPitchMm"],Math.PI*3),"normal pitch");
        var helixTip=Input("helical_external","OutsideDiameterMm",h.Values["PitchDiameterMm"]+6,"Teeth",20,"HelixAngleDeg",30,"NormalPressureAngleDeg",20); helixTip.ModuleSystem="normal"; helixTip.StandardUnshiftedFullDepth=true;
        Check(Near(GearingCalculator.Calculate(helixTip).Values["NormalModuleMm"],3),"normal-system helical OD inverse under declared standard assumptions");
        var hinv=GearingCalculator.Calculate(Input("helical_internal","PitchDiameterMm",h.Values["PitchDiameterMm"],"Teeth",20,"HelixAngleDeg",30,"BaseDiameterMm",h.Values["BaseDiameterMm"]));
        Check(Near(hinv.Values["NormalModuleMm"],3) && Near(hinv.Values["NormalPressureAngleDeg"],20),"helical inverse from reference geometry");
        var anglesOnly=GearingCalculator.Calculate(Input("helical_external","NormalPressureAngleDeg",20,"TransversePressureAngleDeg",h.Values["TransversePressureAngleDeg"]));
        Check(Near(anglesOnly.Values["HelixAngleDeg"],30),"two reference pressure planes constrain beta");
        var straightLimit=GearingCalculator.Calculate(Input("helical_external","NormalPressureAngleDeg",20,"TransversePressureAngleDeg",20));
        Check(Near(straightLimit.Values["HelixAngleDeg"],0) && straightLimit.Status!="conflict","zero beta limit is stable");
        var noBeta=GearingCalculator.Calculate(Input("helical_external","PitchDiameterMm",60,"Teeth",20));
        Check(Near(noBeta.Values["TransverseModuleMm"],3) && !noBeta.Values.ContainsKey("NormalModuleMm"),"beta missing leaves normal module unknown");
        var bevel=Input("bevel_straight","ModuleMm",3,"Teeth",20,"MateTeeth",40,"ShaftAngleDeg",90,"PressureAngleDeg",20,"FaceWidthMm",20); bevel.StandardUnshiftedFullDepth=true;
        var b=GearingCalculator.Calculate(bevel);
        Check(Near(b.Values["ConeAngleDeg"],26.565051177078),"bevel pair cone angle");
        Check(Near(b.Values["ConeAngleDeg"]+b.Values["MateConeAngleDeg"],90),"bevel shaft angle constraint");
        Check(Near(b.Values["ConeDistanceMm"],67.0820393249937),"large end cone distance");
        Check(Near(b.Values["MeanModuleMm"],2.5527864045),"mean and large-end modules distinct");
        var worm=Input("worm_pair","AxialModuleMm",3,"WormStarts",2,"WheelTeeth",30,"WormPitchDiameterMm",44,"NormalPressureAngleDeg",20,"ShaftAngleDeg",90); worm.StandardUnshiftedFullDepth=true;
        var w=GearingCalculator.Calculate(worm);
        Check(Near(w.Values["LeadMm"],18.8495559215388),"worm lead includes number of starts");
        Check(Near(w.Values["LeadAngleDeg"],7.7651660184253),"published axial worm lead angle");
        Check(Near(w.Values["AxialPressureAngleDeg"],20.170236268),"worm axial vs normal angle");
        Check(Near(w.Values["WheelPitchDiameterMm"],90) && Near(w.Values["CenterDistanceMm"],67) && Near(w.Values["Ratio"],15),"orthogonal zero-shift worm pair");
        var ratioStart=GearingCalculator.Calculate(Input("worm_pair","Ratio",15,"WheelTeeth",30));
        Check(Near(ratioStart.Values["WormStarts"],2),"worm starts from ratio and wheel teeth");
        var nw=GearingCalculator.Calculate(Input("worm_pair","NormalModuleMm",3,"WormStarts",2,"WheelTeeth",30,"WormPitchDiameterMm",44,"NormalPressureAngleDeg",20,"ShaftAngleDeg",90));
        Check(nw.Values["LeadAngleDeg"]>w.Values["LeadAngleDeg"] && nw.Values["AxialModuleMm"]>3,"normal worm uses asin not axial atan");
        var unknownW=GearingCalculator.Calculate(Input("worm_pair","OutsideDiameterMm",100,"WheelTeeth",30));
        Check(!unknownW.Values.ContainsKey("AxialModuleMm"),"worm wheel outside diameter is not spur OD");
        var non90=Input("worm_pair","AxialModuleMm",3,"WheelTeeth",30,"ShaftAngleDeg",60);
        Check(GearingCalculator.Calculate(non90).Status=="conflict","nonorthogonal worm pair not silently handled");
        var conflicts=GearingCalculator.Calculate(Input("spur_external","ModuleMm",2,"Teeth",24,"PitchDiameterMm",50));
        Check(conflicts.Status=="conflict" && conflicts.Values["PitchDiameterMm"]==50,"conflict preserves input instead of overwrite");
        var start=Input("spur_external","Teeth",24,"ReferenceAngleDeg",10,"FeatureAngleDeg",32.5);
        Check(!GearingCalculator.Calculate(start).Values.ContainsKey("StartAngleDeg"),"no phase without datum and view direction");
        start.PhaseDatum="key centerline"; start.ViewingDirection="+Z toward origin"; start.PositiveRotation="CCW"; start.StartFeature="tooth 1 centerline";
        var angles=GearingCalculator.Calculate(start);
        Check(Near(angles.Values["StartAngleDeg"],22.5) && Near(angles.Values["PhaseWithinPitchDeg"],7.5),"full orientation and tooth-period phase distinct");
        Check(!spur.Values.ContainsKey("StartAngleDeg"),"module/pressure do not imply start angle");
        var coordinate=Input("spur_external","Teeth",24,"CenterXmm",0,"CenterYmm",0,"DatumXmm",0,"DatumYmm",10,"FeatureXmm",20,"FeatureYmm",0);
        coordinate.PhaseDatum="key point"; coordinate.ViewingDirection="front +Z"; coordinate.PositiveRotation="CCW"; coordinate.StartFeature="named tooth center";
        Check(Near(GearingCalculator.Calculate(coordinate).Values["StartAngleDeg"],270),"phase from original drawing coordinates");
        coordinate.PositiveRotation="CW";
        Check(Near(GearingCalculator.Calculate(coordinate).Values["StartAngleDeg"],90),"clockwise coordinate convention");
        coordinate.Measurements["DatumYmm"]=0;
        Check(GearingCalculator.Calculate(coordinate).Status=="conflict","zero-length datum vector is not an angle");
        var working=Input("spur_external","ModuleMm",2,"Teeth",20,"MateTeeth",40,"PressureAngleDeg",20,"CenterDistanceMm",62);
        var work=GearingCalculator.Calculate(working);
        Check(work.Values["OperatingPressureAngleDeg"]>20 && Near(work.Values["PressureAngleDeg"],20),"working mesh angle does not overwrite reference angle");
        var ambiguous=Input("spur_external","ModuleMm",1,"PitchDiameterMm",303.5,"PressureAngleDeg",20); ambiguous.RelativeTolerance=.01;
        Check(!GearingCalculator.Calculate(ambiguous).Values.ContainsKey("Teeth"),"multiple integers within tolerance are not rounded to one");
        var inconsistentStandard=Input("spur_external","ProfileShift",.3,"ModuleMm",2,"Teeth",24); inconsistentStandard.StandardUnshiftedFullDepth=true;
        Check(GearingCalculator.Calculate(inconsistentStandard).Status=="conflict","shift contradicts unshifted assumption");
        bool invalid=false; try { GearingCalculator.Calculate(Input("spur_external","Teeth",24.5)); } catch(ArgumentException) { invalid=true; }
        Check(invalid,"fractional tooth count rejected");
        Console.WriteLine("PASS: "+count+" gearing formula checks");
    }
}
