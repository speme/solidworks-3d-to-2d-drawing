using System;
using System.IO;
using System.Linq;
using AutoDrawing;
class Checks
{
    static int count;
    static void Check(bool condition,string message) { count++; if(!condition) throw new Exception(message); }
    static DimensionEvidence Dim(string name,string config,string identity) { return new DimensionEvidence { Model="part.sldprt",Configuration=config,FullName=name,Identity=identity,Semantic="length|25" }; }
    static void Main()
    {
        Check(new DrawingOptions().DirectChamfers,"默认直接标倒角");
        Check(!ChamferRules.CanProjectCone("Imported",false),"导入圆锥不得进入不稳定轮廓 API");
        Check(!ChamferRules.CanProjectCone("MBimport",true),"仅名称匹配不能放行导入面");
        Check(!ChamferRules.CanProjectCone("BaseBody",true),"VH400 实际 BaseBody 类型必须在轮廓 API 前分流");
        Check(!ChamferRules.CanProjectCone("",false),"来源不明圆锥不得进入轮廓 API");
        Check(!ChamferRules.CanProjectCone("Revolution",false),"几何识别圆锥须保留待补标");
        Check(!ChamferRules.CanProjectCone("Chamfer",false),"原生类型还须匹配源特征记录");
        Check(ChamferRules.CanProjectCone("Chamfer",true),"可追溯原生圆锥倒角保持支持");
        Check(new DrawingOptions().DirectFillets && new DrawingOptions().OptimizeAnnotationLayout,"默认 R 标注及线条避让");
        Check(new DrawingOptions().RackParameters.Count==0,"齿条参数默认留空，不猜制造规格");
        var rackSamples=new System.Collections.Generic.List<RackPlaneSample>();
        for(int i=0;i<20;i++) foreach(double sign in new[]{-1.0,1.0}) rackSamples.Add(new RackPlaneSample { Normal=new[]{.94,0,sign*.342},Point=new[]{i*.015708,0,.005},AreaMm2=500 });
        rackSamples.Add(new RackPlaneSample { Normal=new[]{0.0,0,1},Point=new[]{0.0,0,.05},AreaMm2=20000 });
        var rack=RackRules.Detect(rackSamples,new[]{0.0,-50,0,400,0,50});
        Check(rack!=null && rack.LengthAxis==0 && rack.WidthAxis==1,"由相反周期齿面识别齿条方向");
        Check(Math.Abs(rack.MeasuredPitchMm-15.708)<1e-6 && rack.RepeatedIntervals==19,"实测节距与连续周期证据");
        Check(rack.BackFaceConfirmed && rack.BackAtMaximum,"以大平面确认孔距背面基准");
        Check(RackRules.Detect(rackSamples.Where(s=>s.Normal[2]>=0).ToList(),new[]{0.0,-50,0,400,0,50})==null,"单侧周期倒角不能误识别齿条");
        Check(RackRules.Detect(rackSamples,new[]{0.0,-50,0,100,0,100})==null,"短宽轮类不按齿条处理");
        bool badRack=false; try { RackRules.Validate(new System.Collections.Generic.Dictionary<string,string> { {"Module","0"} }); } catch(ArgumentException) { badRack=true; }
        Check(badRack,"拒绝无效齿条模数");
        var unknownDim=Dim("C1","A","c1"); unknownDim.ValueVerified=false;
        Check(DimensionRules.Key(unknownDim)==null,"无法回读的倒角不得参与数值去重");
        Check(LineRules.Crosses(new PaperLine(0,0,.02,.02),new PaperLine(0,.02,.02,0)),"识别标注线交叉");
        Check(!LineRules.Crosses(new PaperLine(0,0,.02,0),new PaperLine(.02,0,.02,.02)),"连接端点不能误报交叉");
        Check(Math.Abs(LineRules.Overlap(new PaperLine(0,0,.02,0),new PaperLine(.01,0,.03,0))-.01)<1e-9,"识别共线重合长度");
        Check(LineRules.Overlap(new PaperLine(0,0,.02,0),new PaperLine(0,.002,.02,.002))==0,"平行错开尺寸线不可误报重合");
        GearRules.Validate(new System.Collections.Generic.Dictionary<string,string> { {"Module","2"},{"Teeth","24"},{"PressureAngle","20"} });
        bool invalidGear=false; try { GearRules.Validate(new System.Collections.Generic.Dictionary<string,string> { {"Teeth","24.5"} }); } catch(ArgumentException) { invalidGear=true; }
        Check(invalidGear,"齿数必须为整数，不能猜测或舍入");
        invalidGear=false; try { GearRules.Validate(new System.Collections.Generic.Dictionary<string,string> { {"Modul","2"} }); } catch(ArgumentException) { invalidGear=true; }
        Check(invalidGear,"齿轮参数拼写错误须明确拒绝");
        var first=LayoutPlanner.Cells(.420,.297,true); var third=LayoutPlanner.Cells(.420,.297,false);
        Check(first[1].Y<first[0].Y,"第一角俯视图应在主视图下面");
        Check(third[1].Y>third[0].Y,"第三角俯视图应在主视图上面");
        Check(first.All(b=>b.Y>=.065 && b.X>=.025 && b.X+b.W<=.405001 && b.Y+b.H<=.282001),"图框和标题栏区域");
        Check(LayoutPlanner.Fit(new[]{Tuple.Create(.8,.4,first[0])})<=.2,"大模型缩放需要留出尺寸通道");
        Check(DimensionRules.Key(Dim("D1@Sketch1","A","one"))!=DimensionRules.Key(Dim("D2@Sketch1","A","two")),"同值异源不可去重");
        Check(DimensionRules.Key(Dim("D1@Sketch1","A","one"))!=DimensionRules.Key(Dim("D1@Sketch1","B","one")),"不同配置不可去重");
        Check(DimensionRules.Key(Dim("D1@Sketch1","A","one"))!=DimensionRules.Key(Dim("D1@Sketch1","A","two")),"不同装配实例不可去重");
        Check(DimensionRules.Key(Dim("D1@Sketch1","A",""))==null,"缺少持久身份不得删除");
        var d=Dim("D1@Sketch1","A","one"); d.Dangling=true;
        Check(DimensionRules.Key(d)==null,"悬空尺寸须保留");
        var a=Dim("D1@Sketch1","A","one"); var changed=Dim("D1@Sketch1","A","one"); changed.Semantic="angle|25";
        Check(DimensionRules.Key(a)!=DimensionRules.Key(changed),"不同标注语义不可去重");
        Check(DimensionRules.Key(a)==DimensionRules.Key(Dim("D1@Sketch1","A","one")),"同源同义可去重");
        var g1=Dim("RD1@front","A","body1"); g1.GeometryKey="overall:body1:x:0:0.02";
        var g2=Dim("RD2@top","A","body1"); g2.GeometryKey=g1.GeometryKey;
        Check(DimensionRules.Key(g1)==DimensionRules.Key(g2),"经证实的整体尺寸可跨视图去重");
        g2.Identity="body2";
        Check(DimensionRules.Key(g1)!=DimensionRules.Key(g2),"不同实体整体尺寸不可合并");
        var graph=new DimensionConstraintGraph();
        graph.Add("plane:A","plane:B","length-AB");
        graph.Add("plane:B","center:C","location-BC");
        Check(graph.Path("plane:A","center:C").SequenceEqual(new[]{"length-AB","location-BC"}),"闭合链必须提供保留尺寸路径");
        Check(graph.Path("center:C","plane:A").Count==2,"反向测量关系等效");
        Check(graph.Path("plane:A","center:other-hole")==null,"同值不同孔位没有删除依据");
        Check(graph.Path("other-body:A","plane:B")==null,"不同实体不得合并");
        Check(graph.Path("plane:A","plane:A")==null,"零长度及不明关系不参与精简");
        Check(graph.Path("plane:A","plane:B").SequenceEqual(new[]{"length-AB"}),"同一几何关系可直接去重");
        var r=new RecognitionReport { BoundsMm=new[]{0.0,0,0,100,10,60} };
        r.Geometry.Add(new RecognizedGeometry { Id="h1",Body=0,Kind="hole",Axis=new[]{0.0,-1,0},Center=new[]{.02,0.0,.03},Extent="through",DiameterMm=8,DepthMm=10 });
        // Canonical signs allow coaxial tests to compare opposite cylinder parameterizations.
        Check(RecognitionRules.Axis(new[]{0.0,-2,0})[1]==1,"轴向符号应规范化");
        r.Geometry[0].Axis=RecognitionRules.Axis(r.Geometry[0].Axis);
        Check(RecognitionRules.ChooseView(r)=="Top","侧向孔应选择能正视孔的主视图");
        var h2=new RecognizedGeometry { Id="h2",Body=0,Kind="hole",Axis=new[]{0.0,1,0},Center=new[]{.02,.01,.03},Extent="partial",DiameterMm=12,DepthMm=6 };
        Check(RecognitionRules.AxisLine(r.Geometry[0])==RecognitionRules.AxisLine(h2),"同轴孔应忽略轴线起点沿轴的偏移");
        h2.Body=1;
        Check(RecognitionRules.AxisLine(r.Geometry[0])!=RecognitionRules.AxisLine(h2),"不同实体孔不可按同轴孔合并");
        h2.Body=0; r.Geometry.Add(h2); RecognitionRules.Finish(r);
        Check(r.Notes.Count==1,"阶梯孔候选识别");
        Check(RecognitionRules.Category("HoleWzd")=="hole_wizard" && RecognitionRules.Category("Thread")=="thread_feature","特征语义分类");
        var endHole=new RecognitionReport { BoundsMm=new[]{0.0,-15,-15,140,15,15} };
        endHole.Geometry.Add(new RecognizedGeometry { Kind="hole",Axis=new[]{1.0,0,0},OpenEndView="Left" });
        Check(RecognitionRules.ChooseView(endHole)=="Left","单端孔必须优先从实际开口侧观察，避免螺纹落在遮挡端");
        Check(RecognitionRules.Category("MBimport")=="imported","供应商导入实体类型也应归类");
        Check(BatchPlanner.IsModel("part.SLDPRT") && !BatchPlanner.IsModel("~$part.SLDPRT") && !BatchPlanner.IsModel("part.step"),"文件发现应排除锁文件及其他格式");
        var source=new DrawingOptions { Configuration="Original",AllConfigurations=true };
        var clone=BatchPlanner.ForJob(source,new BatchJob { Model="part.sldprt",Configuration="Thin" });
        Check(clone.Configuration=="Thin" && !clone.AllConfigurations && source.Configuration=="Original" && source.AllConfigurations,"每个配置的选项必须独立，避免污染后续任务");
        bool rejected=false; try { LayoutPlanner.Cells(.21,.15,true,.14); } catch(ArgumentException) { rejected=true; }
        Check(rejected,"模板标题栏侵占视图区时必须阻断");
        Console.WriteLine("PASS: "+count+" checks");
    }
}
