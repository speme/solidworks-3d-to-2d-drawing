# 图片研究与实现记录

## 螺纹漏标修复（1.2.3）

供应商导入实体 `SFJC30-130-M10-SC12.sat<1>` 下有吸收子特征 `SupplierThread_1_Internal_M10x1p5`，类型 `CosmeticThread`。原生 `ThreadCallout` 为 `M10x1.5 INTERNAL; DEPTH 20 mm`，直径 10 mm、深度 20 mm；原几何孔直径为 8.376 mm、圆柱段深度 27.5 mm，不能拿它替代螺纹规格。

漏标来自两个独立缺口：特征识别只读顶层；非 ANSI 模板并不保证自动创建螺纹文字。新增递归遍历和显式标注。此模型中 `InsertThreadCallout` 选择真实 CThread 后仍未产生文字，`CThread.ThreadCallout` 保持 null；不能假装 API 已成功。经真机验证，选中原生装饰螺纹后 `InsertNote` 可创建直接关联 CThread 的引线文字，其附着类型为 `swSelCTHREADS=39`、有 1 条引线、未悬空。保存重开后验证同一注释名称、文本和实体关联。报告保留专门表达标识和人工复核要求。

源零件只读，不更改螺纹定义；规格全文取自原生特征，而非由文件名或圆柱半径补造。PDF 用全文和原生位置关联文字框；DXF 独立读取 MTEXT/TEXT 检查规格和深度。

官方依据：[装饰螺纹及制图标准行为](https://help.solidworks.com/2024/english/SolidWorks/sldworks/c_cosmetic_threads.htm)、[原生螺纹来源文字](https://help.solidworks.com/2026/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.ICosmeticThreadFeatureData~ThreadCallout.html)、[原生文字插入接口](https://help.solidworks.com/2020/English/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IDrawingDoc~InsertThreadCallout.html)。

## 1.1 补充研究与真机证据（2026-11-16）

新增 `Recognition.cs` 从真实 BRep 读取曲面类型、圆柱轴线、UV 完整角域、面法向、圆边和相邻平面。内外圆柱用 `EvaluateAtPoint` 的法向与径向点积，并按 `FaceInSurfaceSense` 修正方向；不能仅用后者的布尔值直接断定孔。腰槽必须有两个半圆柱端面及共同的两个侧壁，特意排除共同顶/底面，避免把两个独立半圆凹口误识别为槽。

孔范围通过圆边中心的轴向值与单实体轴向包围盒比较；完整平底盲孔还要求邻接单环底面面积与圆面积吻合。复杂斜孔、钻尖盲孔和分割面保守记录未知/局部，不推断公差与螺纹等级。同轴不同直径圆柱只报告阶梯孔候选。原生特征分类保留特征名和类型，可用于与几何结果相互核对。

主视图先比较孔槽轴线与标准观察方向，再比较投影面积。使用 `CreateUnfoldedViewAt3(x,y,z,false)` 生成关联投影视图，避免换主视图后仍套用固定前/俯/左命名组合。配置展开实际切换模型、读取其当前 BRep 并按配置建立图纸，最后恢复原配置。

独立自动标注实验 `tests/probe_autodim.py` 比较 4 组合法参数，每组都生成 6 个尺寸且返回 1。不能把 1 改写成成功枚举，也不能为了返回码而重复添加尺寸。1.1 明确记录前/后数量，仅零新增时重试一次。

PDF 复核发现轴件的一个尺寸位于纸外；原生 AutoArrange 并不保证文字落入图纸。新增按视图单元钳位标注位置并保存重开验证。PDF 关联使用最大匹配数、最小总距离的一对一分配，修正贪心最近匹配可能抢占其他尺寸唯一候选的问题。尺寸符号数字解析扩展到 R/Φ/Ø/角度，同时拒绝 M6、零件编号及复杂公差，避免冒认普通字符串。

原生回归夹具与报告位于 `artifacts/v1.1/fixtures/`、`artifacts/v1.1/configurations/`：5 类几何样件全部通过，双厚度配置的实测边界分别为 10 和 20 mm；全部最终 PDF 尺寸文字关联完成、未检测到文字框碰撞。几何与标注持久引用无法直接对应的情况继续显示待复核，不把数值一致升级为可靠覆盖。

官方补充依据：[投影视图 API](https://help.solidworks.com/2022/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IDrawingDoc~CreateUnfoldedViewAt3.html)、[面法向方向合同](https://help.solidworks.com/2021/english/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IFace2~FaceInSurfaceSense.html)、[官方自动标注示例](https://help.solidworks.com/2021/English/api/sldworksapi/Autodimension_Selected_Drawing_View_Example_CSharp.htm)。

## 能从图片确定的需求

八张 WebP 包含功能宣传、批处理窗口、三种零件出图样例。宣传明确提出批量出图、特征识别标注、跨视图几何追踪去重、同类倒角 `n X Cx` 合并、孤立基准清理和重建后不复活。截图提供了输入列表、工程图模板、输出目录、标注选项和开始按钮。样例展示板件、带孔支座、柱件、三视图、轴测图、标题栏、孔引线与大量基线尺寸。

截图不能证明原产品使用了哪些 API、是否覆盖所有配置、是否有完整几何内核或是否通过所有制造规范。此实现将可观察需求映射到可测试的 SolidWorks 原生操作。

## 实现路线及取舍

采用 .NET Framework 4.8 x64、SolidWorks PIA、`ISwAddin`、WinForms 中文配置窗口。使用原生工程图对象和显示尺寸，模型引用保持可编辑关联。没有采用图片识别生成静态 DXF，也没有以普通 Note 假装真实尺寸。

一次任务顺序为：校验输入 → 只读打开模型/复用已打开模型 → 检查保存状态与配置 → 从模板创建工程图 → 设置投影法 → 创建视图并回读配置和边界 → 计算统一比例及位置 → 导入尺寸/几何自动标注 → 语义去重 → 可选倒角前缀 → 自动排列 → 重建检查 → 保存原生图和 PDF → 重开验证视图与尺寸数 → 写入复核报告。

参考截图的“几何血统”落实为两种证据：真实模型尺寸持久引用；以及范围严格受限的单实体整体尺寸证明。对任意特征进行拓扑等价推断仍未实现。因为导入模型和工程图参考尺寸的参数名不同，仅按 FullName 去重不能清除几何整体重复；模型参考点和轴向边界比较解决了测试板的这类问题。孔距等局部尺寸不使用此规则。

原截图的“孤立基准清理”风险较高：坐标尺寸基准可影响整条尺寸链。本实现仅检测悬空标注并提示，不直接删除。后续应回读尺寸依赖图并确认基准无任何关联再清理。

## API 查证

本地依据：`C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS\SolidWorks.Interop.*.dll`，程序集版本 `34.0.0.5070`。实现前反射核对签名和枚举，编译器进一步核对参数、COM 返回接口及类型转换。

| API | 参数及合同 | 实测 |
|---|---|---|
| `CreateDrawViewFromModelView3` | 模型绝对路径、精确命名视图、米制位置 | 中文环境需 `*前视/*上视/*左视/*等轴测` |
| `InsertModelAnnotations3` | 来源、类型掩码、AllViews、DuplicateDims、HiddenFeatureDims、UsePlacementInSketch | **DuplicateDims=true 是消除重复**，名称容易误读；空模型参数并不产生尺寸 |
| `AutoDimension` | 实体范围、水平方案/放置、垂直方案/放置 | API 34 实测会产生尺寸且返回 1，必须记录异常状态 |
| `IDimension.ReferencePoints` | SAFEARRAY of MathPoint；读取，不改写 | 本机取得模型坐标中的参考点 |
| `DimensionLineDirection` | 模型空间尺寸线方向 | 用于区分 X/Y/Z；不按相同数值推断同一尺寸 |
| `GetPersistReference3` | 模型参数/实体持久引用字节 | 未取得引用时不执行相应删除规则 |
| `DeleteSelection2(0)` | 只删除已选择的工程图显示标注；不请求吸收/子特征删除 | 板件删 3 个重复项，重建和保存重开不复活 |
| `AlignDimensions` | AutoArrange=0、间距米 | PDF 真机检查 |
| `SaveAs` | 保存返回值、错误码、警告码、落盘大小 | SLDDRW 和 PDF 导出、重开验证通过 |

独立审查读取每个最终尺寸的参数 ID、视图、模型、配置、类型、SI 数值、COM 位置、持久引用与几何键；PDF 审查按实际值和位置关联数字文字框。匹配率和碰撞报告都不会替代制造语义审核。

官方资料：

- [ISwAddin](https://help.solidworks.com/2026/English/api/swpublishedapi/SOLIDWORKS.Interop.swpublished~SOLIDWORKS.Interop.swpublished.ISwAddin.html)
- [创建模型视图](https://help.solidworks.com/2020/English/api/sldworksapi/SOLIDWORKS.Interop.sldworks~SOLIDWORKS.Interop.sldworks.IDrawingDoc~CreateDrawViewFromModelView3.html)
- [模型标注导入](https://help.solidworks.com/2016/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IDrawingDoc~InsertModelAnnotations3.html)
- [工程图自动标注](https://help.solidworks.com/2024/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IDrawingDoc~AutoDimension.html)
- [标注排列](https://help.solidworks.com/2016/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.imodeldocextension~aligndimensions.html)
- [自动标注状态枚举](https://help.solidworks.com/2024/English/api/swconst/SOLIDWORKS.Interop.swconst~SOLIDWORKS.Interop.swconst.swAutodimStatus_e.html)

## 下一轮优化依据

先扩大回归模型：沉孔、螺纹盲孔、腰槽、圆柱阶梯、非轴对齐零件、多实体、多配置和重复装配实例。随后补孔槽特征规格/数量/定位覆盖矩阵、配置级实体引用缓存、尺寸链可达性、完整 PDF 原生符号及公差匹配、尺寸文字框避让和标题栏业务字段。倒角合并需要对每条物理棱边建立计数证据，而不是仅依靠特征面数量。

批处理性能主要受 SolidWorks 单线程内核、重建、导出影响。下一步可在一件内部缓存稳定几何和持久引用，将 COM 批量读结果交给纯算法；不能并行调用同一个 SolidWorks 实例写文档。
