using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;

namespace AutoDrawing
{
    public sealed class GearingInput
    {
        public string Family { get; set; }
        public string ModuleSystem { get; set; }
        public bool StandardUnshiftedFullDepth { get; set; }
        public Dictionary<string,double> Measurements { get; set; }
        public Dictionary<string,string> Evidence { get; set; }
        public string PhaseDatum { get; set; }
        public string ViewingDirection { get; set; }
        public string PositiveRotation { get; set; }
        public string StartFeature { get; set; }
        public string Hand { get; set; }
        public double RelativeTolerance { get; set; }
        public GearingInput() { Measurements=new Dictionary<string,double>(); Evidence=new Dictionary<string,string>(); RelativeTolerance=1e-6; }
    }
    public sealed class GearingDerivation
    {
        public string Quantity { get; set; }
        public double Value { get; set; }
        public string Formula { get; set; }
        public Dictionary<string,double> Inputs { get; set; }
        public List<string> Sources { get; set; }
        public string Condition { get; set; }
    }
    public sealed class GearingResult
    {
        public string Family { get; set; }
        public string Status { get; set; }
        public Dictionary<string,double> Values { get; set; }
        public List<GearingDerivation> Derivations { get; set; }
        public List<string> Issues { get; set; }
        public List<string> Missing { get; set; }
        public GearingInput Input { get; set; }
        public GearingResult() { Values=new Dictionary<string,double>(); Derivations=new List<GearingDerivation>(); Issues=new List<string>(); Missing=new List<string>(); }
    }
    public static class GearingCalculator
    {
        public static readonly string[] Families={"rack_straight","spur_external","spur_internal","helical_external","helical_internal","bevel_straight","worm_pair"};
        public static readonly string[] Quantities={"ModuleMm","Teeth","MateTeeth","PitchDiameterMm","MatePitchDiameterMm","BaseDiameterMm","MateBaseDiameterMm","OutsideDiameterMm","TipDiameterMm","RootDiameterMm","CircularPitchMm","BasePitchMm","PressureAngleDeg","NormalPressureAngleDeg","TransversePressureAngleDeg","OperatingPressureAngleDeg","NormalModuleMm","TransverseModuleMm","HelixAngleDeg","HelixLeadMm","ShaftAngleDeg","ConeAngleDeg","MateConeAngleDeg","ConeDistanceMm","FaceWidthMm","MeanModuleMm","WormStarts","WheelTeeth","WormPitchDiameterMm","WheelPitchDiameterMm","AxialModuleMm","AxialPitchMm","LeadMm","LeadAngleDeg","AxialPressureAngleDeg","AxialDiameterQuotient","CenterDistanceMm","Ratio","RackFlankSlope","FirstToothCenterFromStartMm","ReferenceAngleDeg","FeatureAngleDeg","ProfileShift","CenterXmm","CenterYmm","DatumXmm","DatumYmm","FeatureXmm","FeatureYmm"};
        private static readonly string[] counts={"Teeth","MateTeeth","WormStarts","WheelTeeth"};
        private static double Rad(double a) { return a*Math.PI/180; }
        private static double Deg(double a) { return a*180/Math.PI; }
        private static double Mod(double a,double p) { return ((a%p)+p)%p; }
        private static double Unit(double value) { return value>1 && value<1+1e-12?1:value< -1 && value> -1-1e-12?-1:value; }
        public static void Validate(GearingInput input)
        {
            if(input==null || !Families.Contains(input.Family) || input.Measurements==null || input.Evidence==null) throw new ArgumentException("请选择支持的齿制并提供尺寸字典。");
            if(double.IsNaN(input.RelativeTolerance) || double.IsInfinity(input.RelativeTolerance) || input.RelativeTolerance<1e-12 || input.RelativeTolerance>.01) throw new ArgumentException("一致性相对阈值须在 1e-12 至 0.01 之间，不能代替原图公差校验。");
            if(input.ModuleSystem!=null && !new[]{"","normal","transverse","axial"}.Contains(input.ModuleSystem)) throw new ArgumentException("ModuleSystem 必须为 normal/transverse/axial 或留空。");
            if(input.PositiveRotation!=null && !new[]{"","CCW","CW"}.Contains(input.PositiveRotation)) throw new ArgumentException("PositiveRotation 必须为 CCW/CW 或留空。");
            if(input.Hand!=null && !new[]{"","RH","LH"}.Contains(input.Hand)) throw new ArgumentException("Hand 必须为 RH/LH 或留空。");
            foreach(var p in input.Measurements) {
                if(!Quantities.Contains(p.Key) || double.IsNaN(p.Value) || double.IsInfinity(p.Value)) throw new ArgumentException("未知或非有限测量值："+p.Key);
                bool signed=new[]{"ReferenceAngleDeg","FeatureAngleDeg","HelixAngleDeg","ProfileShift","CenterXmm","CenterYmm","DatumXmm","DatumYmm","FeatureXmm","FeatureYmm"}.Contains(p.Key);
                bool zeroAllowed=new[]{"RackFlankSlope","FirstToothCenterFromStartMm"}.Contains(p.Key);
                if(!signed && (zeroAllowed?p.Value<0:p.Value<=0)) throw new ArgumentException("测量值超出范围："+p.Key);
                if(counts.Contains(p.Key) && (p.Value!=Math.Truncate(p.Value) || p.Value>100000)) throw new ArgumentException("齿数/头数必须为正整数："+p.Key);
                if(p.Key.EndsWith("AngleDeg") && !new[]{"ReferenceAngleDeg","FeatureAngleDeg","ShaftAngleDeg"}.Contains(p.Key) && (Math.Abs(p.Value)>=90 || p.Key!="HelixAngleDeg" && p.Value<=0)) throw new ArgumentException("角度须在有效范围内："+p.Key);
                if(p.Key=="ShaftAngleDeg" && p.Value>=180) throw new ArgumentException("轴交角须小于 180°。");
            }
        }
        private sealed class Solver
        {
            public GearingResult Result;
            public bool Conflict;
            private readonly HashSet<string> messages=new HashSet<string>();
            public Solver(GearingInput input) { Result=new GearingResult { Family=input.Family,Input=input }; foreach(var p in input.Measurements) Result.Values[p.Key]=p.Value; }
            public void Issue(string text,bool conflict=false) { if(messages.Add(text)) Result.Issues.Add(text); if(conflict) Conflict=true; }
            public bool Has(params string[] names) { return names.All(Result.Values.ContainsKey); }
            public double V(string key) { return Result.Values[key]; }
            public void Offer(string target,string formula,string[] inputs,Func<double> calculate,string condition="")
            {
                if(!Has(inputs)) return;
                double value=calculate();
                if(double.IsNaN(value) || double.IsInfinity(value) || value<0 && !new[]{"ProfileShift"}.Contains(target)) { Issue("公式输入不成立："+formula,true); return; }
                if(target.Contains("PressureAngle") && (value<=0 || value>=90) || new[]{"ConeAngleDeg","MateConeAngleDeg"}.Contains(target) && (value<=0 || value>=90)) { Issue("角度结果不在该公式有效范围："+target,true); return; }
                if(target=="LeadAngleDeg" && (value<=0 || value>=90) || target=="HelixAngleDeg" && value>=90) { Issue("导程/螺旋角结果不在有效范围："+target,true); return; }
                if(counts.Contains(target)) {
                    double error=Math.Max(1e-6,Math.Abs(value)*Result.Input.RelativeTolerance),low=Math.Ceiling(value-error),high=Math.Floor(value+error);
                    if(low>high || high<1) { Issue("齿数/头数结果不是整数："+target+"；"+formula,true); return; }
                    if(low!=high) { Issue("给定数值阈值内有多个整数，未唯一确定齿数/头数："+target); return; }
                    value=low;
                }
                if(Result.Values.ContainsKey(target)) {
                    double old=target=="HelixAngleDeg"?Math.Abs(V(target)):V(target);
                    double difference=new[]{"ReferenceAngleDeg","FeatureAngleDeg","StartAngleDeg"}.Contains(target)?Math.Abs(Mod(old-value+180,360)-180):Math.Abs(old-value);
                    if(difference>Math.Max(1e-6,Math.Max(Math.Abs(old),Math.Abs(value))*Result.Input.RelativeTolerance)) Issue("尺寸约束冲突："+target+"；"+formula,true);
                    return;
                }
                Result.Values[target]=value;
                Result.Derivations.Add(new GearingDerivation { Quantity=target,Value=value,Formula=formula,Inputs=inputs.ToDictionary(k=>k,V),Condition=condition,
                    Sources=inputs.Select(k=>Result.Input.Evidence.ContainsKey(k) && !string.IsNullOrWhiteSpace(Result.Input.Evidence[k])?Result.Input.Evidence[k]:Result.Input.Measurements.ContainsKey(k)?"input:"+k:"derived:"+k).ToList() });
            }
            public void Relation(string product,string a,string b)
            {
                Offer(product,product+"="+a+"*"+b,new[]{a,b},()=>V(a)*V(b));
                Offer(a,a+"="+product+"/"+b,new[]{product,b},()=>V(product)/V(b));
                Offer(b,b+"="+product+"/"+a,new[]{product,a},()=>V(product)/V(a));
            }
            public void Pitch(string pitch,string module)
            {
                Offer(pitch,pitch+"=pi*"+module,new[]{module},()=>Math.PI*V(module));
                Offer(module,module+"="+pitch+"/pi",new[]{pitch},()=>V(pitch)/Math.PI);
            }
        }
        public static GearingResult Calculate(GearingInput input)
        {
            Validate(input); var s=new Solver(input); string family=input.Family;
            if(input.StandardUnshiftedFullDepth && s.Has("ProfileShift") && Math.Abs(s.V("ProfileShift"))>1e-9) s.Issue("声明无变位标准全齿高，但 ProfileShift 不为 0。",true);
            bool helical=family.StartsWith("helical"),worm=family=="worm_pair",internalGear=family.EndsWith("internal");
            if(helical || worm) {
                string module=input.ModuleSystem=="normal"?"NormalModuleMm":input.ModuleSystem=="transverse" && helical?"TransverseModuleMm":input.ModuleSystem=="axial" && worm?"AxialModuleMm":null;
                if(module!=null) s.Offer(module,module+"=ModuleMm (declared basis)",new[]{"ModuleMm"},()=>s.V("ModuleMm"));
                else if(s.Has("ModuleMm")) s.Issue("模数基面未声明，未把 ModuleMm 当作法向、端面或轴向模数。");
                if(s.Has("PressureAngleDeg")) s.Issue("斜齿/蜗杆请使用明确的 Normal/Transverse/AxialPressureAngleDeg，未自动解释通用压力角。");
            }
            for(int pass=0;pass<12;pass++) {
                int before=s.Result.Values.Count;
                if(!helical && !worm) {
                    s.Pitch("CircularPitchMm","ModuleMm");
                    if(family!="rack_straight") s.Relation("PitchDiameterMm","ModuleMm","Teeth");
                    if(family!="bevel_straight") {
                        s.Offer("BasePitchMm","pb=p*cos(alpha)",new[]{"CircularPitchMm","PressureAngleDeg"},()=>s.V("CircularPitchMm")*Math.Cos(Rad(s.V("PressureAngleDeg"))));
                        s.Offer("PressureAngleDeg","alpha=acos(pb/p)",new[]{"BasePitchMm","CircularPitchMm"},()=>Deg(Math.Acos(s.V("BasePitchMm")/s.V("CircularPitchMm"))));
                        s.Offer("CircularPitchMm","p=pb/cos(alpha)",new[]{"BasePitchMm","PressureAngleDeg"},()=>s.V("BasePitchMm")/Math.Cos(Rad(s.V("PressureAngleDeg"))));
                    }
                    if(family.StartsWith("spur")) {
                        s.Offer("BaseDiameterMm","db=d*cos(alpha)",new[]{"PitchDiameterMm","PressureAngleDeg"},()=>s.V("PitchDiameterMm")*Math.Cos(Rad(s.V("PressureAngleDeg"))));
                        s.Offer("PressureAngleDeg","alpha=acos(db/d)",new[]{"BaseDiameterMm","PitchDiameterMm"},()=>Deg(Math.Acos(s.V("BaseDiameterMm")/s.V("PitchDiameterMm"))));
                        s.Offer("PitchDiameterMm","d=db/cos(alpha)",new[]{"BaseDiameterMm","PressureAngleDeg"},()=>s.V("BaseDiameterMm")/Math.Cos(Rad(s.V("PressureAngleDeg"))));
                    }
                }
                if(family=="rack_straight") {
                    s.Offer("PressureAngleDeg","alpha=atan(abs(n_height/n_length))",new[]{"RackFlankSlope"},()=>Deg(Math.Atan(Math.Abs(s.V("RackFlankSlope")))));
                    s.Offer("RackPhaseMm","s0=first-center-offset mod p",new[]{"FirstToothCenterFromStartMm","CircularPitchMm"},()=>Mod(s.V("FirstToothCenterFromStartMm"),s.V("CircularPitchMm")));
                    s.Offer("LinearPitchPhaseDeg","linear phase=360*s0/p (not a body rotation)",new[]{"RackPhaseMm","CircularPitchMm"},()=>360*s.V("RackPhaseMm")/s.V("CircularPitchMm"));
                }
                if(family.StartsWith("spur") && input.StandardUnshiftedFullDepth) {
                    double tip=internalGear?-2:2,root=internalGear?2.5:-2.5;
                    string diameter=internalGear?"TipDiameterMm":"OutsideDiameterMm";
                    s.Offer(diameter,"da=m*(z"+(tip>0?"+2":"-2")+")",new[]{"ModuleMm","Teeth"},()=>s.V("ModuleMm")*(s.V("Teeth")+tip),"explicit standard, unshifted, full depth, no tip modification; internal da is INNER tooth-tip diameter");
                    s.Offer("ModuleMm","m=da/(z"+(tip>0?"+2":"-2")+")",new[]{diameter,"Teeth"},()=>s.V(diameter)/(s.V("Teeth")+tip),"explicit standard, unshifted, full depth, no tip modification");
                    s.Offer("Teeth","z=da/m-tip_factor",new[]{diameter,"ModuleMm"},()=>s.V(diameter)/s.V("ModuleMm")-tip,"explicit standard, unshifted, full depth");
                    s.Offer("RootDiameterMm","df=m*(z"+(root>0?"+2.5":"-2.5")+")",new[]{"ModuleMm","Teeth"},()=>s.V("ModuleMm")*(s.V("Teeth")+root),"explicit standard, unshifted, full depth");
                    s.Offer("ModuleMm","m=df/(z+root_factor)",new[]{"RootDiameterMm","Teeth"},()=>s.V("RootDiameterMm")/(s.V("Teeth")+root),"explicit standard, unshifted, full depth");
                }
                if(family.StartsWith("spur") || helical) {
                    s.Relation("MatePitchDiameterMm",helical?"TransverseModuleMm":"ModuleMm","MateTeeth");
                    string alpha=helical?"TransversePressureAngleDeg":"PressureAngleDeg";
                    s.Offer("MateBaseDiameterMm","db2=d2*cos(reference alpha_t)",new[]{"MatePitchDiameterMm",alpha},()=>s.V("MatePitchDiameterMm")*Math.Cos(Rad(s.V(alpha))));
                    s.Offer("OperatingPressureAngleDeg",internalGear?"alpha_w=acos(abs(db2-db1)/(2*a))":"alpha_w=acos((db1+db2)/(2*a))",new[]{"BaseDiameterMm","MateBaseDiameterMm","CenterDistanceMm"},()=>Deg(Math.Acos((internalGear?Math.Abs(s.V("MateBaseDiameterMm")-s.V("BaseDiameterMm")):s.V("BaseDiameterMm")+s.V("MateBaseDiameterMm"))/(2*s.V("CenterDistanceMm")))),"working angle is distinct from reference pressure angle");
                    s.Offer("OperatingPitchDiameterMm",internalGear?"dw1=2*a*z1/abs(z2-z1)":"dw1=2*a*z1/(z1+z2)",new[]{"CenterDistanceMm","Teeth","MateTeeth"},()=>2*s.V("CenterDistanceMm")*s.V("Teeth")/(internalGear?Math.Abs(s.V("MateTeeth")-s.V("Teeth")):s.V("Teeth")+s.V("MateTeeth")));
                }
                if(helical) {
                    s.Relation("PitchDiameterMm","TransverseModuleMm","Teeth"); s.Pitch("CircularPitchMm","TransverseModuleMm"); s.Pitch("NormalPitchMm","NormalModuleMm");
                    s.Offer("NormalModuleMm","mn=mt*cos(beta)",new[]{"TransverseModuleMm","HelixAngleDeg"},()=>s.V("TransverseModuleMm")*Math.Cos(Rad(s.V("HelixAngleDeg"))));
                    s.Offer("TransverseModuleMm","mt=mn/cos(beta)",new[]{"NormalModuleMm","HelixAngleDeg"},()=>s.V("NormalModuleMm")/Math.Cos(Rad(s.V("HelixAngleDeg"))));
                    s.Offer("TransversePressureAngleDeg","alpha_t=atan(tan(alpha_n)/cos(beta))",new[]{"NormalPressureAngleDeg","HelixAngleDeg"},()=>Deg(Math.Atan(Math.Tan(Rad(s.V("NormalPressureAngleDeg")))/Math.Cos(Rad(s.V("HelixAngleDeg"))))));
                    s.Offer("NormalPressureAngleDeg","alpha_n=atan(tan(alpha_t)*cos(beta))",new[]{"TransversePressureAngleDeg","HelixAngleDeg"},()=>Deg(Math.Atan(Math.Tan(Rad(s.V("TransversePressureAngleDeg")))*Math.Cos(Rad(s.V("HelixAngleDeg"))))));
                    s.Offer("TransversePressureAngleDeg","alpha_t=acos(db/d)",new[]{"BaseDiameterMm","PitchDiameterMm"},()=>Deg(Math.Acos(s.V("BaseDiameterMm")/s.V("PitchDiameterMm"))));
                    s.Offer("BaseDiameterMm","db=d*cos(alpha_t)",new[]{"PitchDiameterMm","TransversePressureAngleDeg"},()=>s.V("PitchDiameterMm")*Math.Cos(Rad(s.V("TransversePressureAngleDeg"))));
                    s.Offer("PitchDiameterMm","d=db/cos(alpha_t)",new[]{"BaseDiameterMm","TransversePressureAngleDeg"},()=>s.V("BaseDiameterMm")/Math.Cos(Rad(s.V("TransversePressureAngleDeg"))));
                    s.Offer("HelixAngleDeg","abs(beta)=atan(pi*d/helix_lead)",new[]{"PitchDiameterMm","HelixLeadMm"},()=>Deg(Math.Atan(Math.PI*s.V("PitchDiameterMm")/s.V("HelixLeadMm"))));
                    s.Offer("HelixAngleDeg","abs(beta)=acos(mn/mt)",new[]{"NormalModuleMm","TransverseModuleMm"},()=>Deg(Math.Acos(Unit(s.V("NormalModuleMm")/s.V("TransverseModuleMm")))));
                    s.Offer("HelixAngleDeg","abs(beta)=acos(tan(alpha_n)/tan(alpha_t))",new[]{"NormalPressureAngleDeg","TransversePressureAngleDeg"},()=>Deg(Math.Acos(Unit(Math.Tan(Rad(s.V("NormalPressureAngleDeg")))/Math.Tan(Rad(s.V("TransversePressureAngleDeg")))))));
                    if(s.Has("HelixAngleDeg") && Math.Abs(s.V("HelixAngleDeg"))>1e-10) s.Offer("HelixLeadMm","helix_lead=pi*d/tan(abs(beta))",new[]{"PitchDiameterMm","HelixAngleDeg"},()=>Math.PI*s.V("PitchDiameterMm")/Math.Tan(Rad(Math.Abs(s.V("HelixAngleDeg")))));
                    if(input.StandardUnshiftedFullDepth && new[]{"normal","transverse"}.Contains(input.ModuleSystem)) {
                        string module=input.ModuleSystem=="normal"?"NormalModuleMm":"TransverseModuleMm",diameter=internalGear?"TipDiameterMm":"OutsideDiameterMm";
                        double tip=internalGear?-2:2,root=internalGear?2.5:-2.5;
                        s.Offer(diameter,"da=d+tip_factor*m_basis",new[]{"PitchDiameterMm",module},()=>s.V("PitchDiameterMm")+tip*s.V(module),"explicit unshifted standard full depth in declared "+input.ModuleSystem+" system");
                        s.Offer("RootDiameterMm","df=d+root_factor*m_basis",new[]{"PitchDiameterMm",module},()=>s.V("PitchDiameterMm")+root*s.V(module),"explicit unshifted standard full depth in declared "+input.ModuleSystem+" system");
                        if(input.ModuleSystem=="normal") s.Offer("NormalModuleMm","mn=da/(z/cos(beta)+tip_factor)",new[]{diameter,"Teeth","HelixAngleDeg"},()=>s.V(diameter)/(s.V("Teeth")/Math.Cos(Rad(s.V("HelixAngleDeg")))+tip),"explicit unshifted standard normal-system full depth");
                        else s.Offer("TransverseModuleMm","mt=da/(z+tip_factor)",new[]{diameter,"Teeth"},()=>s.V(diameter)/(s.V("Teeth")+tip),"explicit unshifted standard transverse-system full depth");
                    }
                }
                if(family=="bevel_straight") {
                    s.Relation("MatePitchDiameterMm","ModuleMm","MateTeeth");
                    s.Offer("ConeAngleDeg","delta1=atan2(sin(Sigma),z2/z1+cos(Sigma))",new[]{"ShaftAngleDeg","Teeth","MateTeeth"},()=>Deg(Math.Atan2(Math.Sin(Rad(s.V("ShaftAngleDeg"))),s.V("MateTeeth")/s.V("Teeth")+Math.Cos(Rad(s.V("ShaftAngleDeg"))))));
                    s.Offer("MateConeAngleDeg","delta2=Sigma-delta1",new[]{"ShaftAngleDeg","ConeAngleDeg"},()=>s.V("ShaftAngleDeg")-s.V("ConeAngleDeg"));
                    s.Offer("ConeDistanceMm","R=d1/(2*sin(delta1))",new[]{"PitchDiameterMm","ConeAngleDeg"},()=>s.V("PitchDiameterMm")/(2*Math.Sin(Rad(s.V("ConeAngleDeg")))));
                    s.Offer("PitchDiameterMm","d1=2*R*sin(delta1)",new[]{"ConeDistanceMm","ConeAngleDeg"},()=>2*s.V("ConeDistanceMm")*Math.Sin(Rad(s.V("ConeAngleDeg"))));
                    s.Offer("ConeAngleDeg","delta1=asin(d1/(2*R))",new[]{"PitchDiameterMm","ConeDistanceMm"},()=>Deg(Math.Asin(s.V("PitchDiameterMm")/(2*s.V("ConeDistanceMm")))));
                    s.Offer("ShaftAngleDeg","Sigma=delta1+delta2",new[]{"ConeAngleDeg","MateConeAngleDeg"},()=>s.V("ConeAngleDeg")+s.V("MateConeAngleDeg"));
                    s.Offer("MeanModuleMm","m_mean=m*(1-b/(2*R))",new[]{"ModuleMm","FaceWidthMm","ConeDistanceMm"},()=>s.V("ModuleMm")*(1-s.V("FaceWidthMm")/(2*s.V("ConeDistanceMm"))),"straight external bevel; large-end reference module");
                    if(input.StandardUnshiftedFullDepth) {
                        s.Offer("ModuleMm","m=da/(z+2*cos(delta))",new[]{"OutsideDiameterMm","Teeth","ConeAngleDeg"},()=>s.V("OutsideDiameterMm")/(s.V("Teeth")+2*Math.Cos(Rad(s.V("ConeAngleDeg")))),"explicit unshifted standard straight bevel, large end");
                        s.Offer("OutsideDiameterMm","da=m*(z+2*cos(delta))",new[]{"ModuleMm","Teeth","ConeAngleDeg"},()=>s.V("ModuleMm")*(s.V("Teeth")+2*Math.Cos(Rad(s.V("ConeAngleDeg")))),"explicit unshifted standard straight bevel, large end");
                    }
                }
                if(worm) {
                    s.Pitch("AxialPitchMm","AxialModuleMm"); s.Relation("LeadMm","AxialPitchMm","WormStarts"); s.Relation("WormPitchDiameterMm","AxialModuleMm","AxialDiameterQuotient");
                    if(s.Has("ShaftAngleDeg")) { if(Math.Abs(s.V("ShaftAngleDeg")-90)<1e-6) s.Relation("WheelPitchDiameterMm","AxialModuleMm","WheelTeeth"); else s.Issue("当前 worm_pair 公式只覆盖正交圆柱蜗杆副，未应用非 90° 轴交关系。",true); }
                    s.Offer("LeadAngleDeg","gamma=atan(L/(pi*d1))",new[]{"LeadMm","WormPitchDiameterMm"},()=>Deg(Math.Atan(s.V("LeadMm")/(Math.PI*s.V("WormPitchDiameterMm")))));
                    s.Offer("LeadAngleDeg","gamma=asin(mn*z1/d1)",new[]{"NormalModuleMm","WormStarts","WormPitchDiameterMm"},()=>Deg(Math.Asin(Unit(s.V("NormalModuleMm")*s.V("WormStarts")/s.V("WormPitchDiameterMm")))));
                    s.Offer("LeadMm","L=pi*d1*tan(gamma)",new[]{"WormPitchDiameterMm","LeadAngleDeg"},()=>Math.PI*s.V("WormPitchDiameterMm")*Math.Tan(Rad(s.V("LeadAngleDeg"))));
                    s.Offer("WormPitchDiameterMm","d1=L/(pi*tan(gamma))",new[]{"LeadMm","LeadAngleDeg"},()=>s.V("LeadMm")/(Math.PI*Math.Tan(Rad(s.V("LeadAngleDeg")))));
                    s.Offer("NormalModuleMm","mn=mx*cos(gamma)",new[]{"AxialModuleMm","LeadAngleDeg"},()=>s.V("AxialModuleMm")*Math.Cos(Rad(s.V("LeadAngleDeg"))));
                    s.Offer("AxialModuleMm","mx=mn/cos(gamma)",new[]{"NormalModuleMm","LeadAngleDeg"},()=>s.V("NormalModuleMm")/Math.Cos(Rad(s.V("LeadAngleDeg"))));
                    s.Offer("NormalPressureAngleDeg","alpha_n=atan(tan(alpha_x)*cos(gamma))",new[]{"AxialPressureAngleDeg","LeadAngleDeg"},()=>Deg(Math.Atan(Math.Tan(Rad(s.V("AxialPressureAngleDeg")))*Math.Cos(Rad(s.V("LeadAngleDeg"))))));
                    s.Offer("AxialPressureAngleDeg","alpha_x=atan(tan(alpha_n)/cos(gamma))",new[]{"NormalPressureAngleDeg","LeadAngleDeg"},()=>Deg(Math.Atan(Math.Tan(Rad(s.V("NormalPressureAngleDeg")))/Math.Cos(Rad(s.V("LeadAngleDeg"))))));
                    s.Offer("Ratio","i=z2/z1",new[]{"WheelTeeth","WormStarts"},()=>s.V("WheelTeeth")/s.V("WormStarts"));
                    s.Relation("WheelTeeth","Ratio","WormStarts");
                    if(input.StandardUnshiftedFullDepth && s.Has("ShaftAngleDeg") && Math.Abs(s.V("ShaftAngleDeg")-90)<1e-6) s.Offer("CenterDistanceMm","a=(d1+d2)/2",new[]{"WormPitchDiameterMm","WheelPitchDiameterMm"},()=> (s.V("WormPitchDiameterMm")+s.V("WheelPitchDiameterMm"))/2,"explicit zero profile shift; orthogonal cylindrical reference geometry");
                }
                if(before==s.Result.Values.Count) break;
            }
            if(family!="rack_straight" && !string.IsNullOrWhiteSpace(input.PositiveRotation)) foreach(var name in new[]{"Datum","Feature"}) {
                string x=name+"Xmm",y=name+"Ymm",target=name=="Datum"?"ReferenceAngleDeg":"FeatureAngleDeg";
                s.Offer(target,"angle=atan2(y-yc,x-xc), drawing plane x right/y up, converted to declared positive direction",new[]{x,y,"CenterXmm","CenterYmm"},()=> {
                    double dx=s.V(x)-s.V("CenterXmm"),dy=s.V(y)-s.V("CenterYmm");
                    if(dx*dx+dy*dy<1e-18) return double.NaN;
                    return Mod((input.PositiveRotation=="CW"?-1:1)*Deg(Math.Atan2(dy,dx)),360);
                });
            }
            if(family!="rack_straight" && s.Has("ReferenceAngleDeg","FeatureAngleDeg")) {
                if(string.IsNullOrWhiteSpace(input.PhaseDatum) || string.IsNullOrWhiteSpace(input.ViewingDirection) || string.IsNullOrWhiteSpace(input.PositiveRotation) || string.IsNullOrWhiteSpace(input.StartFeature)) s.Issue("起始角缺少命名起始特征、零位基准、观察方向或正向；未计算唯一相位。");
                else {
                    s.Offer("StartAngleDeg","theta0=(feature-reference) mod 360; input angles follow declared positive direction",new[]{"ReferenceAngleDeg","FeatureAngleDeg"},()=>Mod(s.V("FeatureAngleDeg")-s.V("ReferenceAngleDeg"),360),input.PhaseDatum+"; "+input.ViewingDirection+"; "+input.PositiveRotation+"; "+input.StartFeature);
                    string count=worm?"WormStarts":"Teeth";
                    s.Offer("PhaseWithinPitchDeg","phase=theta0 mod (360/count)",new[]{"StartAngleDeg",count},()=>Mod(s.V("StartAngleDeg"),360/s.V(count)),"identical teeth/starts repeat; not an independently identified first tooth");
                }
            }
            string[] required=worm?new[]{"AxialModuleMm","NormalModuleMm","NormalPressureAngleDeg","WormStarts","WheelTeeth","LeadAngleDeg","ShaftAngleDeg","WheelPitchDiameterMm"}:helical?new[]{"NormalModuleMm","NormalPressureAngleDeg","HelixAngleDeg","Teeth"}:family=="rack_straight"?new[]{"ModuleMm","PressureAngleDeg"}:new[]{"ModuleMm","PressureAngleDeg","Teeth"};
            s.Result.Missing=required.Where(k=>!s.Has(k)).ToList();
            if(family!="rack_straight" && !s.Has("StartAngleDeg") && (s.Has("ReferenceAngleDeg") || s.Has("FeatureAngleDeg") || !string.IsNullOrWhiteSpace(input.PhaseDatum))) s.Result.Missing.Add("StartAngleDeg");
            if(!s.Has("StartAngleDeg") && family!="rack_straight") s.Issue("起始角不是模数/压力角的函数，仍需指定起始特征及其基准观测。");
            if(worm) s.Issue("仅计算圆柱蜗杆的参考几何；蜗轮喉圆/外径、ZA/ZN/ZI/ZK 等型式、包络与修形未由这些尺寸唯一确定。");
            if(!input.StandardUnshiftedFullDepth && s.Has("OutsideDiameterMm") && !s.Has("ModuleMm") && !helical && !worm) s.Issue("未确认无变位标准全齿高，未使用齿顶圆和齿数反推模数。");
            s.Result.Status=s.Conflict?"conflict":s.Result.Missing.Count>0?"underdetermined":"review_required";
            return s.Result;
        }
        public static int RunCli(string[] args)
        {
            try {
                string request=null,output=null;
                for(int i=0;i<args.Length;i+=2) { if(i+1>=args.Length) throw new ArgumentException("缺少参数值。"); if(args[i]=="--request") request=args[i+1]; else if(args[i]=="--result") output=args[i+1]; else throw new ArgumentException("未知计算参数："+args[i]); }
                if(request==null) throw new ArgumentException("calculate-gearing 需要 --request。");
                var json=new JavaScriptSerializer { MaxJsonLength=16*1024*1024 }; var raw=json.Deserialize<Dictionary<string,object>>(File.ReadAllText(request));
                foreach(string key in raw.Keys) if(!typeof(GearingInput).GetProperties().Any(p=>p.Name==key)) throw new ArgumentException("未知公式输入字段："+key);
                var result=Calculate(json.ConvertToType<GearingInput>(raw)); string text=json.Serialize(result);
                if(output!=null) { using(var stream=new FileStream(output,FileMode.CreateNew,FileAccess.Write)) using(var writer=new StreamWriter(stream,new UTF8Encoding(false))) writer.Write(text); }
                Console.WriteLine(text); return result.Status=="conflict"?4:0;
            } catch(Exception ex) { Console.Error.WriteLine(ex.Message); return 2; }
        }
    }
}
