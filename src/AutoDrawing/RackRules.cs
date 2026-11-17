using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace AutoDrawing
{
    public sealed class RackPlaneSample
    {
        public double[] Normal { get; set; }
        public double[] Point { get; set; }
        public double AreaMm2 { get; set; }
    }
    public sealed class RackGeometryEvidence
    {
        public int LengthAxis { get; set; }
        public int WidthAxis { get; set; }
        public int HeightAxis { get; set; }
        public double MeasuredPitchMm { get; set; }
        public int RepeatedIntervals { get; set; }
        public string Source { get; set; }
        public bool BackFaceConfirmed { get; set; }
        public bool BackAtMaximum { get; set; }
        public double MeasuredFlankSlope { get; set; }
    }
    public static class RackRules
    {
        public static readonly string[] Keys={"Module","PressureAngle","PitchLineFromBack","HelixAngle","Accuracy","Material","HeatTreatment","StartEnd"};
        public static void Validate(Dictionary<string,string> values)
        {
            if(values==null) throw new ArgumentException("RackParameters 不能为 null。");
            foreach(var p in values) {
                if(!Keys.Contains(p.Key) || string.IsNullOrWhiteSpace(p.Value)) throw new ArgumentException("齿条参数无效："+p.Key);
                if(p.Key=="StartEnd" && p.Value!="Minimum" && p.Value!="Maximum") throw new ArgumentException("StartEnd 必须为 Minimum/Maximum。");
                if(!new[]{"Module","PressureAngle","PitchLineFromBack","HelixAngle"}.Contains(p.Key)) continue;
                double n;
                if(!double.TryParse(p.Value,NumberStyles.Float,CultureInfo.InvariantCulture,out n) || double.IsNaN(n) || double.IsInfinity(n) ||
                   p.Key=="Module" && (n<=0 || n>1000) || p.Key=="PressureAngle" && (n<=0 || n>=45) || p.Key=="PitchLineFromBack" && n<=0 || p.Key=="HelixAngle" && Math.Abs(n)>=90) throw new ArgumentException("齿条数值参数无效："+p.Key);
            }
        }
        private sealed class Series { public double Slope,Pitch,Area; public int Run; }
        public static RackGeometryEvidence Detect(List<RackPlaneSample> samples,double[] boundsMm)
        {
            if(boundsMm==null || samples==null) return null;
            var size=Enumerable.Range(0,3).Select(i=>boundsMm[i+3]-boundsMm[i]).ToArray();
            int length=Enumerable.Range(0,3).OrderByDescending(i=>size[i]).First();
            if(size[length]<3.5*Enumerable.Range(0,3).Where(i=>i!=length).Max(i=>size[i])) return null;
            RackGeometryEvidence best=null; double bestArea=0;
            foreach(int width in Enumerable.Range(0,3).Where(i=>i!=length)) {
                int height=3-length-width;
                var groups=samples.Where(s=>s.Normal!=null && Math.Abs(s.Normal[width])<1e-7 && Math.Abs(s.Normal[length])>.15 && Math.Abs(s.Normal[height])>.05)
                    .GroupBy(s=>Math.Round(s.Normal[height]/s.Normal[length],6));
                var series=new List<Series>();
                foreach(var group in groups) {
                    var stations=group.Select(s=>Enumerable.Range(0,3).Sum(i=>s.Normal[i]*s.Point[i])/s.Normal[length]*1000).OrderBy(x=>x).ToList();
                    var unique=new List<double>(); foreach(double x in stations) if(unique.Count==0 || x-unique.Last()>1e-5) unique.Add(x);
                    if(unique.Count<8) continue;
                    var deltas=unique.Skip(1).Zip(unique,(a,b)=>a-b).Where(x=>x>.2 && x<size[length]/3).ToList();
                    if(deltas.Count<7) continue;
                    double pitch=deltas.GroupBy(x=>Math.Round(x,3)).OrderByDescending(g=>g.Count()).First().Average();
                    int run=0,longest=0;
                    foreach(double gap in unique.Skip(1).Zip(unique,(a,b)=>a-b)) { run=Math.Abs(gap-pitch)<Math.Max(1e-4,pitch*1e-5)?run+1:0; longest=Math.Max(longest,run); }
                    if(longest>=7) series.Add(new Series { Slope=group.Average(s=>s.Normal[height]/s.Normal[length]),Pitch=pitch,Run=longest,Area=group.Sum(s=>s.AreaMm2) });
                }
                foreach(var a in series.Where(s=>s.Slope>0)) foreach(var b in series.Where(s=>s.Slope<0)) {
                    if(Math.Abs(a.Slope+b.Slope)>1e-5 || Math.Abs(a.Pitch-b.Pitch)>Math.Max(1e-4,a.Pitch*1e-5)) continue;
                    double area=Math.Min(a.Area,b.Area); if(area<=bestArea) continue;
                    bestArea=area; best=new RackGeometryEvidence { LengthAxis=length,WidthAxis=width,HeightAxis=height,MeasuredPitchMm=(a.Pitch+b.Pitch)/2,MeasuredFlankSlope=(Math.Abs(a.Slope)+Math.Abs(b.Slope))/2,RepeatedIntervals=Math.Min(a.Run,b.Run),Source="paired_periodic_planar_flanks" };
                }
            }
            if(best!=null) {
                var back=samples.Where(s=>Math.Abs(Math.Abs(s.Normal[best.HeightAxis])-1)<1e-7 &&
                    (Math.Abs(s.Point[best.HeightAxis]*1000-boundsMm[best.HeightAxis])<1e-5 || Math.Abs(s.Point[best.HeightAxis]*1000-boundsMm[best.HeightAxis+3])<1e-5))
                    .OrderByDescending(s=>s.AreaMm2).FirstOrDefault();
                if(back!=null && back.AreaMm2>size[best.LengthAxis]*size[best.WidthAxis]*.25) {
                    best.BackFaceConfirmed=true; best.BackAtMaximum=Math.Abs(back.Point[best.HeightAxis]*1000-boundsMm[best.HeightAxis+3])<1e-5;
                }
            }
            return best;
        }
    }
}
