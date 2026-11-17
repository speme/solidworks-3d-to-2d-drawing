# 原图齿轮特性推算与起端基准（1.3.6）

长度输入为 mm，角度输入为 °；代码在三角函数内部转换为弧度。`d` 是**基准分度圆**，不是齿顶圆或工作节圆。推算结果含公式、输入、来源和条件；`review_required` 不是制造批准。不会把结果自动吸附到标准模数系列。

| 类型 | 可用关系 | 必须区分的条件 |
|---|---|---|
| 直齿条 | `m=p/π`；`α=atan(|n高/n长|)` | p 来自多周期配对直齿侧面；法向来自主齿侧面，排除小倒角面。这里不是斜齿条法向模数推断。 |
| 外直齿 | `m=d/z`，`p=πm`，`db=d cosα`，`α=acos(db/d)=acos(pb/p)` | d/db/p/pb 须是同一基准体系。仅有外径和齿数不能确定压力角。 |
| 内直齿 | `d=mz`；标准无变位时内齿齿顶内径 `da=m(z−2)`、齿根圆 `df=m(z+2.5)` | `TipDiameterMm` 是内齿齿顶内径；环体外径不能代入。 |
| 斜齿 | `mt=mn/cosβ`；`d=z mt`；`tanαt=tanαn/cosβ`；`db=d cosαt` | 法向/端面模数与压力角成套使用。`tan|β|=πd/螺旋线导程`，导程须是一整圈的轴向进给。 |
| 直锥齿轮副 | 大端 `d=mz`；`δ1=atan2(sinΣ,z2/z1+cosΣ)`；`δ2=Σ−δ1`；`R=d1/(2sinδ1)`；`m中=m(1−b/(2R))` | 当前为相交轴外啮合直锥；大端与中点模数不能混用。螺旋锥齿、准双曲面不套用这些规则。 |
| 圆柱蜗杆自身 | `px=πmx`，`L=z1 px`，`tanγ=L/(πd1)`，`mn=mx cosγ`；法向已知时 `sinγ=mn z1/d1` | z1 是**头数**；多头蜗杆的导程不是轴向齿距。 |
| 正交圆柱蜗杆副 | `d2=z2 mx`，`i=z2/z1`；`tanαn=tanαx cosγ` | 必须明确轴交角 90°。法向/轴向压力角、模数不同；蜗轮外径/喉圆不能套普通齿轮外径公式。 |

标准外直齿无变位全齿高且未修齿顶时，才启用 `m=da/(z+2)`、`df=m(z−2.5)`。标准斜齿相应使用**声明基面**的齿顶高系数：`da=d±2m基面`，内齿用减号。标准直锥大端用 `da=m(z+2cosδ)`。这些条件由 `StandardUnshiftedFullDepth` 显式声明；不能仅因看起来像标准齿轮就默认启用。

外啮合工作角 `αw=acos((db1+db2)/(2a))`；内啮合用基圆直径差的绝对值。工作节圆 `dw1=2az1/(z1+z2)`，内啮合分母用齿数差绝对值。**αw 不会覆盖基准压力角 α**。仅凭中心距变化不能断定变位量，还可能存在侧隙；程序不从它强行分配两个齿轮的变位系数。

蜗杆副零变位参考几何可用 `a=(d1+d2)/2`，需显式确认。ZA、ZN、ZI、ZK 等齿面型式、包络、喉圆、修形和强度并未因此确定。蜗杆副目前提供公式计算，尚不接受其全自动制造标注；不能据导程角单独判定可靠自锁。

## 起始角与齿条起端

起始角不是模数、压力角的函数。必须给出：命名的起始特征、零位基准、观察方向、正向。已知观测角时 `θ0=wrap360(θ特征−θ基准)`；已知观察平面中的点坐标时，先以 `atan2(y−yc,x−xc)` 求角，再按声明的顺/逆时针约定换算。坐标平面约定 X 向右、Y 向上。

完整起始角与一个齿距内的相位不同：齿轮 `φ=θ0 mod(360/z)`；多头蜗杆用 `360/z1`。没有命名起始特征时，对称齿/螺纹头不能唯一编号。齿条是线性相位 `s0=首齿中心距起端 mod p`；`360s0/p` 只是线性周期的等效角，不是零件旋转角。

图中“起端 S (X=0)”的引线关联到实际端点，并注明 X+ 的纸面方向。`RackParameters.StartEnd=Minimum/Maximum` 选择模型长向最小/最大端，孔表随之重算。默认最小端是明确的出图约定，不表示从对称孔阵列唯一恢复了原图起端。

## 数据入口与能力边界

- 自动：当前长直齿条的周期节距与主齿侧法向可推算 m、α，表内标“计算”，报告记录依据。模型重建误差、非标准齿形仍需核对原图。
- 原图尺寸：界面“齿轮公式…”或 `GearInference.Measurements/Evidence` 输入已知尺寸及图号/DIM 标记，可附加到出图方案。这里没有实现任意扫描图片的自动 OCR。
- 独立计算：`AutoDrawing.Cli.exe calculate-gearing --request observations.json --result calculation.json`，不连接 SolidWorks。
- `conflict` 保留原输入并拒绝自动应用；`underdetermined` 列出尚缺约束；未提供相位不会填 0°。
- `RelativeTolerance` 是数值一致性阈值，不是公差传播。齿数/头数在阈值范围内须有唯一整数才接受。
- 孔表仍是本次几何测量快照，模型变化后须重新生成。代数关系通过测试不等于所有齿轮型式都已完成 CAD 自动识别和制造交付验证。

## 查证来源

公式按制造商公开技术资料交叉核对；本文用统一变量重新整理，未采用目录中的默认压力角作为输入。

1. [KHK：Calculation of Gear Dimensions，4.1–4.6](https://khkgears.net/new/gear_knowledge/gear_technical_reference/calculation_gear_dimensions.html)：直齿、内齿、斜齿、直锥、齿条及蜗杆参考尺寸。
2. [SDP/SI：Elements of Metric Gear Technology](https://sdp-si.com/D805/D805_PDFS/sections/technical.pdf)，§6、§9，特别是 Table 9-2：截面模数、压力角及蜗杆齿距/导程换算。
3. [SDP/SI：Metric Gear Technology](https://www.sdp-si.com/PDFS/Elements-of-Metric-Gear-Technology.pdf)，Table 9-6：轴向模数 3、双头、d1=44 的公开算例用于数值回归。
4. [KHK：Helical Gears](https://khkgears.net/pdf/2025/helical-gears.pdf)：法向和端面模数体系不能混同。
5. [SolidWorks：IAnnotation.SetLeader3](https://help.solidworks.com/2022/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.iannotation~setleader3.html)：起端引线使用原生注解并在重开后核对实体关联。
