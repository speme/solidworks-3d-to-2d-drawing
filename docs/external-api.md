# 外部调用与自定义模板（1.2）

1.3.0：`OptimizeAnnotationLayout`、`DirectFillets`、`AutoReviewPdf` 默认 `true`。前者增加图层/线宽区分和线段冲突统计；`RadiusCallouts` 保存恒定 R 标注及关联/替换/重开证据。`AnnotationLayout` 的 after 值来自保存重开后的原生图形，不代表所有交叉均已消除。

`LineOverlapsBefore/After` 统计非共用界线的重合；`SharedWitnessPairsBefore/After` 仅接受同一视图、相同起点及相同模型持久实体的界线，单独计数。`GeometryOverlaps*` 为尺寸线与投影实体线的共线重合，`Crossings*` 为尺寸间内部交叉。`GeometryReusedOnReopen` 表示实体轮廓通过文件状态、模型更新戳、配置及变换一致性检查后复用，重开尺寸线仍重新读取。

`Options.GearParameters` 为字符串字典，可选键：`Module`、`Teeth`、`PressureAngle`、`HelixAngle`、`ProfileShift`、`GearType`、`Accuracy`、`HeatTreatment`、`Material`、`Hand`。数值使用小数点，模数单位 mm，角度单位度；模数必须为正、齿数为整数。填写项覆盖模型属性。模型属性支持同名英文及“模数/齿数/压力角/螺旋角/变位系数/齿轮类型/精度等级/热处理/材料/旋向”等常用名称。输入不提供压力角时保持缺项，不默认 20°。报告 `Gear` 记录逐项来源、表格内容和保存重开核验。齿轮外形宽度是原生顶点间的测量尺寸，不能拿它替代分度圆直径。见 `examples/gear-job.json`。

1.2.5：`Options.DirectChamfers` 默认 `true`，界面、CLI 和 .NET 使用同一倒角标注流程。45° 输出原生 C 倒角尺寸，其他角度输出距离 × 角度；仅在可追溯标注成功后删除能够证明冗余的本次自动尺寸。显式设为 `false` 可保留原自动尺寸行为。旧 `MergeChamfers` 字段仍接受用于兼容旧请求，不再启用试验性的倒角数量前缀。

每件报告增加 `ChamferCallouts`（来源面、视图、距离、角度、原生样式、关联实体、保存重开状态）与 `RemovedChamferDimensions`（被替换尺寸和对应倒角面），供外部程序审核。Native `Type=10` 表示倒角尺寸；`Dimensions.ValueSI` 为倒角距离（m），`ChamferAngleDegrees` 为角度。DXF 将倒角保存为 `SW_CHAMFER_DIMENSION_*` 块，不能只按 DXF `DIMENSION` 实体计数验证倒角。

`AutoDrawing.Cli.exe --version` 返回运行版本和实际加载 DLL 路径，不连接 SolidWorks。单件和批次报告包含 `GeneratorVersion/GeneratorAssemblyPath`；`Revision` 仍然表示 SolidWorks 的 API 版本。排查旧版本时请核对这两种版本，不要只看宿主 `34.0.0`。

1.2.3：`Jobs[].Recognition.Threads` 返回原生螺纹来源，`Jobs[].ThreadCallouts` 返回文字、来源特征、视图、表达方式和保存重开验证。`native_thread_callout` 是宿主原生引出标注；`cosmetic_thread_with_source_note` 是直接绑定已存在原生 CThread 的带引线来源注释，须工程复核，不声明为自动原生标准文字。无法为已识别螺纹建立标注时任务失败并停止 PDF/DXF 导出。未获得原生规格的导入几何仍不能只按直径推断螺纹。

来源注释保留实体关联，文字是本次生成时读取的规格快照；修改三维螺纹定义后应重新生成工程图，不保证该文字自动随规格更新。当前自动覆盖针对有可读规格、可在正投影视图中回读到 CThread 的零件装饰螺纹；其他真实牙型、装配体逐实例、无规格导入体仍需专项处理和复核。

1.2.2：不指定 `SheetFormat` 时，直接保留 `.drwdot` 内嵌图框。模板中记录的原始 `.slddrt` 路径不可访问不会再强制重载；请求明确指定的图纸格式必须存在。`Jobs[].SheetInitialization` 记录初始化路径、原始属性、属性回读及内嵌图框元素的保存重开校验结果。

`AutoDrawing.Cli.exe` 是 Windows x64 同步命令行入口，与插件界面使用同一出图执行器。无需注册插件。需要已安装 SolidWorks、.NET Framework 4.8 和可交互的 Windows 用户会话；不能作为无桌面的 Windows 服务或 Linux CAD 服务。进程在 STA 线程执行，同一 SolidWorks 实例用互斥锁串行出图。

## 命令行

```powershell
# 直接生成单件
.\AutoDrawing.Cli.exe generate --model 'D:\零件\支架.SLDPRT' --template 'D:\模板\公司A3.drwdot' --output 'D:\图纸'

# 共享模板方案
.\AutoDrawing.Cli.exe generate --model 'D:\零件\支架.SLDPRT' --profile 'D:\模板\company-a3.json' --output 'D:\图纸'

# JSON 批量请求，结果文件必须使用新名称
.\AutoDrawing.Cli.exe generate --request 'D:\任务\job.json' --result 'D:\结果\job-001.json'

# 只校验配置和路径，不连接 SolidWorks，不验证文件的原生 CAD 内容
.\AutoDrawing.Cli.exe validate --request 'D:\任务\job.json'
```

`--model` 可重复使用；`--configuration` 指定全局配置；`--all-configurations` 展开全部配置，两者不能同时使用。`--connect-only` 禁止自动启动 SolidWorks。默认先连接运行实例，连接不到时启动本机默认注册版本，不会在结束时退出用户的 SolidWorks。指定 `--request` 后，只能再添加 `--result`、`--connect-only`；所有其他设置应放进 JSON，避免两套参数冲突。

**stdout 始终是一份 UTF-8 JSON 结果，stderr 是进度。** 调用方应等待进程结束再解析。PowerShell 中可直接读取 `$LASTEXITCODE`；Python/C# 等语言推荐用参数数组启动，不要拼接 shell 命令。`--help` 输出普通帮助文本。

| 退出码 | 含义 |
|---|---|
| 0 | 校验通过，或图纸已生成、状态为 `generated_review_required` |
| 2 | 请求/路径/模板参数无效、未知字段或版本不支持 |
| 3 | 无法连接/启动 SolidWorks |
| 4 | 一个或多个出图任务失败；结果中保留已完成文件及每件错误 |
| 5 | 同一 SolidWorks 实例正在执行其他 AutoDrawing 任务 |
| 6 | 结果 JSON 文件无法写入；stdout 仍返回结果及已有输出路径 |
| 130 | 用户按 Ctrl+C，当前零件完成后停止，结果保留已完成任务 |

成功生成仍需要工程复核，不等同于制造放行。自动标注的宿主返回码异常等警告保留在 `Jobs[].Warnings`。SolidWorks 的同步 COM 调用执行期间不能强行中断；调用方超时后不要直接杀掉共享的 SolidWorks 进程。

## JSON 请求

完整示例见 `examples/job.json`。字段名大小写敏感，未知字段会拒绝，避免拼写错误被静默忽略。

```json
{
  "SchemaVersion": "1.0",
  "RequestId": "bracket-001",
  "TemplateProfile": "company-a3.json",
  "StartSolidWorks": true,
  "StopOnError": false,
  "Models": [{"Path": "models/bracket.SLDPRT", "Configuration": "Default"}],
  "Options": {
    "OutputDirectory": "drawings",
    "DrawingProperties": {"DrawingNo": "{ModelName}", "Designer": "张三"}
  }
}
```

请求文件内相对路径以**请求文件目录**为基准；模板方案中的相对路径以**方案文件目录**为基准；命令行直接传参以当前工作目录为基准。优先级：内置默认值 → 模板方案 `Options` → 请求 `Options`。标题栏属性字典按键合并，其余字段用后者覆盖。单件 `Models[].Configuration` 覆盖全局 `Options.Configuration`。同一路径、同一配置去重，不同配置保留。`AllConfigurations=true` 时不能再指定配置名。

`Options` 支持 `DrawingOptions` 的全部字段，常用项见模板示例。`Template` 必须是存在的 `.drwdot`；`OutputDirectory` 必填；`.slddrw` 及可选 PDF/DXF 文件采用时间和随机 ID 命名，**准确路径以 `Jobs[].Drawing` 为准**。原生 CAD 格式及模板视图内容会在实际生成时验证。

结果包含 `RequestId`、`Status`、`ExitCode`、`ErrorCode`、`Error`、`Planned/Completed/Failed`、`Jobs` 和解析后的选项 `Resolved`。每件 `JobReport` 包含图纸路径、配置、尺寸、识别报告、属性实测值、图幅、图纸格式、阶段耗时及警告。通过替换 `.slddrw` 扩展名可获得对应 `.pdf` 和 `.report.json` 路径；PDF 审查文件见 `Jobs[].PdfReview`。

`--result` 不覆盖已有文件；写入失败时退出码为 6。生成已经完成而写结果失败时，仍应读取 stdout 中的 `Jobs`，避免重复调用。

从 1.2.1 起，默认同时导出 SLDDRW、PDF 和 DXF，无需额外参数。每件成功结果直接返回 `Drawing`、`Pdf`、`Dxf` 路径，DXF 与 PDF 与工程图同目录、同主文件名。路径仅在对应文件保存成功后填入，调用方应同时检查任务状态。确需关闭 DXF 时在 `Options` 中显式指定 `ExportDxf=false`。

## 自定义模板

1. 在 SolidWorks 中编辑公司图框、Logo、标题栏和投影符号，保存为单页、没有预定义视图的 `.drwdot`。修改图纸格式后可单独保存 `.slddrt`。
2. 标题栏需要由外部填写的文字，链接**工程图文档属性**，例如 `$PRP:"DrawingNo"`、`$PRP:"Designer"`。已有 `$PRPSHEET` 从模型取值，不会被工程图属性替换；插件不修改源模型属性。
3. 在主界面点击“自定义模板…”，选择文件，调整图幅、比例、边距和视图间距，编辑属性表格，然后“保存方案…”。JSON 方案可供界面载入及外部调用共用。
4. 可用 `{ModelName}`、`{Configuration}`、`{Date}` 作为属性值占位符。写入前展开为文件名（不含扩展名）、当前配置和本机日期；每项写入后回读，保存重开再次核对。

`SheetFormat` 可省略，使用 `.drwdot` 内已有的图纸格式。`PaperWidthMm` 与 `PaperHeightMm` 同时为 0 表示沿用模板；指定时必须成对填写。更改图幅不会自动缩放公司图框，请提供同幅面的 `.slddrt`。`FixedScale=0` 自动选择比例，`1` 为 1:1，`0.5` 为 1:2；指定比例容不下视图时明确失败，不擅自改小。

布局单位均为 mm：`LeftMarginMm/RightMarginMm/TopMarginMm`、`TitleBlockHeightMm`、`ViewGapMm`、`SpacingMm`。第一角与第三角由 `FirstAngle` 决定；公司模板的投影符号和制图标准应与其一致。版式和美术编辑在 SolidWorks 内完成，此界面管理文件与出图规则。

## .NET 与 Python

可直接引用 `AutoDrawing.dll` 和对应版本的 SolidWorks PIA，从 STA 线程调用：

```csharp
var request = GenerationRequest.Load(requestPath);
var result = GenerationService.Generate(swApp, request, Path.GetDirectoryName(requestPath));
```

调用方提供已连接的 `SldWorks` 对象；`StartSolidWorks` 仅控制 CLI，直接 .NET 调用不创建/退出宿主。`GenerationService.Resolve` 可单独执行路径与规则校验。直接 API 参数校验失败会抛出异常，正常批处理则返回结构化结果。

参见 `examples/call_from_python.py` 和 `examples/call_from_dotnet.cs`。当前提供 CLI/JSON 与 .NET 接口，没有驻留 HTTP 服务。

API 依据：[SetupSheet5](https://help.solidworks.com/2022/english/api/sldworksapi/SolidWorks.Interop.sldworks~SolidWorks.Interop.sldworks.IDrawingDoc~SetupSheet5.html)、[工程图自定义属性 Add3](https://help.solidworks.com/2024/english/api/sldworksapi/solidworks.interop.sldworks~solidworks.interop.sldworks.icustompropertymanager~add3.html)。


`Options.Deduplicate` 默认 `true`：启用同源去重、来源实体与测量方向核验的跨视图自动线性尺寸精简，以及同规格自建圆角数量合并；设为 `false` 可保留全部自动标注。`Jobs[].RemovedRedundantDimensions` 记录删除原因（`same_geometry`、`closed_chain`、`radius_quantity`）、来源节点及保留尺寸路径。`RadiusCallouts.Quantity` 和 `MemberGeometries` 记录圆角数量及实际来源面。此开关不按数值相等删除独立孔位，也不删除本轮之前存在的模型尺寸、公差或自定义文字标注。


1.3.4：圆锥倒角投影仅在源面类型与源特征记录均确认是原生 `Chamfer` 时执行。`Jobs[].SkippedChamfers` 给出未自动标注的候选面、来源类型、轴向/径向几何退距和原因 `unverified_cone_silhouette`。它们不算作已完成倒角标注，原尺寸保留且要求人工补标。`ChamferSilhouetteScans` 记录倒角阶段实际调用视图轮廓扫描的次数；仅含导入圆锥的 VH400 回归应为 0。平面倒角不受这项限制。


1.3.5：`Options.RackParameters` 默认为空字典，支持 `Module`、`PressureAngle`、`PitchLineFromBack`、`HelixAngle`、`Accuracy`、`Material`、`HeatTreatment`。数值为不含单位的字符串，模数/分度线距背面为 mm，角度为度；按键与模板参数合并。参数仅用于识别到的长直齿条。模型属性支持 `RackModule/Module/模数`、`RackPressureAngle/PressureAngle/压力角` 等，显式请求优先。示例见 `examples/rack-job.json`。

`Recognition.RackGeometry` 记录周期平面齿侧、模型长/宽/高轴、实测节距和背面判定；没有给定模数/压力角时仅为 `rack_candidate`。`Jobs[].Rack` 记录参数及来源、实际孔位/规格、参数表和重开核验。孔表的长向坐标从模型最小端计，高向从已确认的大背面计（未确认则从最小面计）。`HoleTableAssociativity=snapshot_at_generation` 表示孔表不是跟随源模型修改的方程关联，模型变化后须重新生成。轴测使用 `Rack.IsometricScale` 独立比例，主投影视图采用图纸比例。

`Dimensions.ValueVerified=false` 表示已有倒角数值回读不可靠，显示文字在 `DisplayText`，原标注保留并要求复核；不得将 `ValueSI` 作为已核验制造尺寸。PDF 审查不会把这些项标记为数值匹配成功。


1.3.6：`calculate-gearing --request observations.json [--result calculation.json]` 不连接 SolidWorks，返回 `Values/Derivations/Issues/Missing/Status`。输入示例见 `examples/gear-observations.json` 和 `examples/worm-calculation.json`。`--result` 不覆盖已有文件；冲突退出 4，无效输入退出 2，欠约束结果仍返回 JSON。

出图 `Options.GearInference` 接受同一输入对象，模板与界面“齿轮公式…”均可配置。`Family` 支持 `rack_straight/spur_external/spur_internal/helical_external/helical_internal/bevel_straight/worm_pair`；`Measurements` 是明确单位的数值字典，`Evidence` 记录原图 DIM/尺寸标记。`StandardUnshiftedFullDepth` 默认 false；`ModuleSystem` 为 normal/transverse/axial，斜齿出图必须明确法向/端面基面。`PhaseDatum/ViewingDirection/PositiveRotation(CCW或CW)/StartFeature` 定义相位基准；可输入 `ReferenceAngleDeg/FeatureAngleDeg`，或观察平面 X向右/Y向上的 `CenterXmm/CenterYmm/DatumXmm/DatumYmm/FeatureXmm/FeatureYmm`。`RelativeTolerance` 默认 1e-6，仅用于数值一致性。详见 `docs/gearing-formulas.md`。

`Jobs[].GearingAnalysis`、`Rack.Calculation` 或 `Gear.Calculation` 记录计算依据。参数与模型/显式参数矛盾时停止应用；缺失相位不会补成 0°。蜗杆副的自动制造标注未验证，当前只使用计算接口，不接受作为普通齿轮出图。

`RackParameters.StartEnd=Minimum/Maximum` 默认为 Minimum；起端引线文字、来源端点及重开结果在 `Rack.StartDatum`，孔表起端坐标按相同选端重算。1.3.6 起，符合直齿条模型的缺项 m/α 可由节距和主齿侧法向推算，表内标“计算”，来源为 `formula:*`，不再一律留空。
