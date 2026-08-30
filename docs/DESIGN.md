# FileManage 详细设计文档

> 版本：v0.9（评审稿） · 日期：2026-08-30
> 目标：替代 FileRenameTool.ps1 V4.0.1，功能对齐并全面增强

---

## 1. 项目概述

### 1.1 定位

Windows 桌面端**文件批量重命名与分类整理工具**，从"一个脚本"升级为"可持续维护的产品"。

### 1.2 目标

| 维度 | 旧版（PS + WinForms） | 新版（C# + WPF） |
|---|---|---|
| 架构 | 单文件 77KB，UI/逻辑混杂 | 三层解耦，引擎可独立测试 |
| 预览 | 单行示例文本 | 全量文件双栏表格，冲突高亮 |
| 分类规则 | 5 组硬编码 | 规则引擎，可自定义/导入导出 |
| 命名能力 | 前缀 + 5 个变量 | 变量扩展 + 替换链 + 正则 |
| 执行安全 | undo.json 单级撤销 | 事务执行（失败回滚）+ 多级撤销 |
| 线程模型 | UI 单线程 | 后台 Task + 进度 + 取消 |
| 分发 | ps2exe（易被杀软误报） | .NET 8 自包含单文件 |

### 1.3 非目标（本期不做）

- 跨平台（macOS/Linux）
- 文件内容编辑、格式转换
- 云盘/网络盘同步

---

## 2. 技术选型

| 项 | 选型 | 说明 |
|---|---|---|
| 运行时 | .NET 8 (LTS) | 自包含单文件发布，`PublishSingleFile` |
| UI | WPF + XAML | 沿用 WinForms 使用经验，生态成熟 |
| MVVM | CommunityToolkit.Mvvm 8.x | 源生成器，少写样板代码 |
| Excel | ClosedXML | 与旧版同库，报表格式可复用 |
| EXIF | MetadataExtractor（NuGet） | 读取照片拍摄时间/GPS |
| 日志 | Serilog（文件滚动） | 崩溃与操作日志分离 |
| 测试 | xUnit + 临时目录集成测试 | 引擎 100% 覆盖 |
| CI | GitHub Actions | build + test + publish 自动打包 |

---

## 3. 总体架构

```
┌─────────────────────────────────────────────┐
│  FileManage.App (WPF, MVVM)                 │
│  Views ── Bind ──> ViewModels               │
│      │ 只做展示与交互，零业务逻辑          │
├───────────────┬─────────────────────────────┤
│               ▼  接口（Core 定义）          │
│  FileManage.Core（纯逻辑，无 UI/无 IO 依赖）│
│  Scanner → Filter → Planner → Conflict      │
│    → Executor（事务）→ UndoStore            │
├───────────────┬─────────────────────────────┤
│               ▼                             │
│  FileManage.Infrastructure                  │
│  JsonConfigStore · ExcelExporter ·          │
│  SeriLogger · ExifService · FileBackup      │
└─────────────────────────────────────────────┘
```

**核心设计原则：**

1. **计划-执行分离**：`Planner` 先产出不可变的 `OperationPlan`（预览所见），`Executor` 只负责忠实执行计划（执行所得），`UndoStore` 由同一份计划生成逆操作（撤销所逆）。三者共享一份数据，杜绝"预览与结果不一致"。
2. **Core 零依赖**：不引用 Windows Forms/WPF，可被控制台 CLI 复用（未来扩展）。
3. **所有 IO 走接口**：Core 依赖 `IFileSystemService` 抽象，测试时可注入内存实现或指向临时目录。

---

## 4. 核心模块设计

### 4.1 FileScanner（扫描器）

```csharp
public record ScanOptions
{
    required string RootDirectory { get; init; }
    int MaxDepth { get; init; } = 0;                 // 0 = 仅当前层（对齐旧版）
    bool IncludeSubdirectories { get; init; }
    IReadOnlyList<string> IncludeGlobs { get; init; } = [];   // 如 *.pdf
    IReadOnlyList<string> ExcludeGlobs { get; init; } = [];   // 如 ~$*、.*;
    long? MinSizeBytes { get; init; }
    long? MaxSizeBytes { get; init; }
    DateTime? ModifiedAfter { get; init; }
    DateTime? ModifiedBefore { get; init; }
}

public FileScanner(IEnumerable<IClassificationRule> rules);
public ScanResult Scan(ScanOptions options, CancellationToken ct);
// ScanResult: IReadOnlyList<FileItem>
// FileItem { FullPath, Name, Extension, Size, ModifiedTime, MatchedCategory?, ExifDate? }
```

- 拖拽文件夹 → 直接作为 `RootDirectory`
- 匹配到分类规则时预填 `MatchedCategory`（预览列即可显示"将被归类到 XX"）

### 4.2 NameEngine（命名引擎）

**两段式：替换链 → 模板渲染**

```
原文件名 ──> [替换链 ReplaceChain] ──> 中间名 ──> [模板 Template] ──> 新文件名
```

#### 4.2.1 模板变量（对齐旧版 + 扩展）

| 变量 | 含义 | 示例 |
|---|---|---|
| `{Prefix}` | UI 前缀框内容 | `合同` |
| `{OriginalName}` | 原文件名（含后缀） | `报告.pdf` |
| `{BaseName}` | 不含后缀 | `报告` |
| `{Extension}` | 后缀（含点） | `.pdf` |
| `{Index}` / `{Counter:000}` | 序号，格式指定位数/起始值 | `001` |
| `{Date:yyyyMMdd}` | 当前日期 | `20260830` |
| `{FileDate:yyyyMMdd}` | 文件修改时间 | `20260830` |
| `{ExifDate:yyyyMMdd}` | EXIF 拍摄时间（无则回退 FileDate） | `20250101` |
| `{ParentDir}` | 父目录名 | `2025年项目` |
| `{Hash8}` | 内容 SHA-256 前 8 位 | `a1b2c3d4` |
| `{Random:N}` | N 位随机字符串 | `x7Kq2p` |

> 旧版三个模板下拉项（前缀+原名 / 前缀+BaseName / 纯前缀+序号）在新版中成为**内置模板预设**，用户可另存自定义模板。

#### 4.2.2 替换链（按顺序对 BaseName 应用）

```csharp
public abstract record ReplaceStep
{
    record LiteralReplace(string Find, string Replacement, bool IgnoreCase);
    record RegexReplace(string Pattern, string Replacement);
    record CaseTransform(CaseMode Mode);        // Upper / Lower / Title
    record TrimSpaces;                          // 合并连续空格、去首尾
    record RemoveChars(string CharSet);         // 去非法/特殊字符
}
```

#### 4.2.3 引擎接口

```csharp
public class NameEngine
{
    public string BuildName(FileItem file, NamingOptions options, int index);
}
public record NamingOptions
{
    string Prefix;
    string Template;               // 默认 "{Prefix}{BaseName}{Extension}"
    IReadOnlyList<ReplaceStep> ReplaceChain;
    bool KeepOriginalExtension;    // 对齐旧版勾选
    int CounterStart = 1;
}
```

### 4.3 RuleEngine（分类规则引擎）

替代旧版硬编码 `FileTypeGroups`，规则持久化于 `rules.json`，可导入导出。

```csharp
public abstract record MatchCondition   // 可组合，AND 语义
{
    record ExtensionIn(params string[] Exts);       // .pdf / .docx
    record NameRegex(string Pattern);
    record SizeBetween(long Min, long Max);
    record DateBetween(DateTime? From, DateTime? To);
}

public record ClassificationRule
{
    Guid Id;
    string Name;                    // "PDF 文档"
    int Priority;                   // 从上到下，首条命中生效
    bool Enabled;
    bool CopyInsteadOfMove;         // true=复制（对齐旧版），false=移动
    string TargetSubfolder;         // 支持变量: "{Category}" "{Date:yyyy}" "{ExifYear}"
    MatchCondition Condition;
}

public class RuleEngine
{
    public ClassificationResult Evaluate(FileItem file);   // 返回 null = 不处理
}
```

**内置默认规则集**（导入即可用，对齐旧版）：

| 规则名 | 条件 | 目标子目录 |
|---|---|---|
| PDF | ext ∈ {.pdf} | `PDF/` |
| WORD | ext ∈ {.doc, .docx} | `WORD/` |
| EXCEL | ext ∈ {.xls, .xlsx} | `EXCEL/` |
| PPT | ext ∈ {.ppt, .pptx} | `PPT/` |
| IMAGE | ext ∈ {.jpg, .jpeg, .png, .tif, .tiff} | `IMAGE/` |

### 4.4 ConflictDetector（冲突检测）

在计划阶段标记 4 类冲突，预览表格红色高亮：

| 类型 | 说明 | 默认处置 |
|---|---|---|
| `PlanDuplicate` | 计划内多个文件映射到同一新名 | 自动追加 `_2`、`_3` 并提示 |
| `TargetExists` | 目标名磁盘上已存在 | 按覆盖策略（询问/全覆盖/全跳过，对齐旧版） |
| `PathTooLong` | 新路径 > 240 字符 | 阻止，标红 |
| `InvalidChars` | 新名含非法字符 | 阻止，标红 |

### 4.5 TransactionExecutor（事务执行器）

```csharp
public class TransactionExecutor
{
    public async Task<ExecutionReport> ExecuteAsync(
        OperationPlan plan,
        OverwritePolicy policy,          // Ask / OverwriteAll / SkipAll
        IProgress<ProgressInfo> progress,
        CancellationToken ct);
}
```

**执行协议（保证可回滚）：**

1. **阶段 A —— 备份**：所有"覆盖/移动"涉及的原文件 → 备份到 `%AppData%/FileManage/backup/{批次ID}/`（重命名不需要备份，只需记录映射）
2. **阶段 B —— 执行**：逐条执行 `Operation`（Rename / Copy / Move），实时上报进度；用户可取消（已执行的不回滚已完成部分，除非出错）
3. **阶段 C —— 提交/回滚**：
   - 全部成功 → 删除备份、写入 `undo` 记录
   - 任一步骤**异常失败**（非用户取消）→ 逆序回滚已完成操作，恢复备份，弹出错误报告
4. **UndoStore**：`%AppData%/FileManage/undo/{批次ID}.json`，记录逆操作链（支持多级撤销，界面列出历史批次）

```csharp
public abstract record Operation          // 计划 = Operation 列表
{
    record RenameOp(string SourcePath, string NewName, string NewPath);
    record CopyOp(string SourcePath, string TargetDir, string TargetName, OverwriteBehavior Overwrite);
    record MoveOp(string SourcePath, string TargetDir, string TargetName);
}
public record UndoBatch(Guid Id, DateTime Time, string Description, IReadOnlyList<UndoAction> Actions);
```

### 4.6 ExcelExporter（报表，对齐旧版）

沿用旧版列结构：序号 / 原路径 / 原文件名 / 新文件名 / 操作 / 分类结果 / 状态 / 失败原因。输出到结果目录，文件名带时间戳。

---

## 5. 数据结构（持久化 Schema）

```jsonc
// %AppData%/FileManage/config.json —— UI 状态与上次设置
{
  "version": 1,
  "lastSourceDir": "D:\\data",
  "lastTargetDir": "D:\\sorted",
  "overwritePolicy": "Ask",
  "uiState": { "windowSize": "1200x800", "theme": "light", "language": "zh-CN" },
  "naming": {
    "prefix": "前缀",
    "template": "{Prefix}{BaseName}{Extension}",
    "keepOriginalExtension": true,
    "replaceChain": []
  },
  "scan": { "maxDepth": 0, "includeGlobs": [], "excludeGlobs": ["~$*"] },
  "configHistory": [ /* 快照，滚动保留 20 份，对齐旧版配置历史 */ ]
}

// %AppData%/FileManage/rules.json —— 分类规则（可导入导出/分享）
{
  "version": 1,
  "rules": [
    {
      "id": "…", "name": "PDF", "priority": 1, "enabled": true,
      "copyInsteadOfMove": true, "targetSubfolder": "PDF",
      "condition": { "type": "extensionIn", "exts": [".pdf"] }
    }
  ]
}
```

---

## 6. 界面设计（线框草图）

### 6.1 主窗口（单窗口三区布局）

```
┌──────────────────────────────────────────────────────────────────┐
│ FileManage  v1.0                                    [主题] [语言]│
├──────────────┬───────────────────────────────────────────────────┤
│ ① 任务导航   │ ② 参数区（随任务切换）                            │
│              │ ┌─ 源目录: [D:\data            ] [浏览...] [▶刷新]│
│ ▸ 重命名     │ │ ☑ 递归子目录  深度[0▾]  排除: [~$*]            │
│ ▸ 分类整理   │ │                                                │
│ ▸ 照片归档   │ │ ── 命名设置 ──────────────────────────────     │
│ ▸ 重复检测   │ │ 前缀: [合同_]  模板: [{Prefix}{BaseName}{Ext}▾] │
│              │ │ ☑ 保留原后缀   [替换规则…] (0)                 │
│              │ │ 示例: 合同_示例文件.pdf                         │
│──────────────│ ├─────────────────────────────────────────────────│
│ ③ 规则/历史  │ │ ④ 预览表格（实时刷新）                         │
│              │ │ ☑ │ 原文件名    │ 新文件名       │ 分类  │ 状态 │
│ [规则管理…]  │ │ ☑ │ 报告.pdf     │ 合同_报告.pdf    │ PDF   │ ✔   │
│ [历史记录…]  │ │ ☑ │ 图1.png      │ 合同_图1.png     │ IMAGE │ ✔   │
│              │ │ ☐ │ 旧.jpg       │ ⚠ 目标已存在     │ IMAGE │ 冲突│
│              │ ├─────────────────────────────────────────────────│
│              │ │ [全选][反选][清空]   123 个文件, 2 个冲突       │
│              │ │ [开始处理] [取消]  ████████░░ 65%  (覆盖:询问▾) │
└──────────────┴───────────────────────────────────────────────────┘
        状态栏: 就绪 │ 上次批次: 20260830_1430 (可撤销) [一键撤销]
```

### 6.2 关键交互

| 场景 | 行为 |
|---|---|
| 修改任意参数 | 300ms 防抖后后台线程重算预览，表格差异高亮刷新 |
| 点击"开始处理" | 弹出确认摘要（将重命名 X 个 / 复制 Y 个 / 覆盖 Z 个）→ 执行 → 完成后可"打开结果目录 / 导出 Excel / 撤销" |
| 执行中 | 进度条 + 当前文件名滚动，[取消] 中止剩余任务 |
| 覆盖询问（Ask） | 模态对话框：此文件/全部覆盖/跳过/全部跳过（对齐旧版 [Show-OverwriteDialog]） |
| 历史记录 | 批次列表 → 选中 → 查看详情 / 撤销该批次（多级撤销） |
| 规则管理 | 独立窗口：规则列表增删改排序，条件编辑器，导入/导出 JSON |

### 6.3 MVVM 结构

```
Views/        MainWindow · PreviewGrid · RuleEditorWindow · HistoryWindow · OverwriteDialog
ViewModels/   MainViewModel · ScanSettingsVM · NamingSettingsVM ·
              PreviewItemVM(每行) · ExecutionVM · RuleEditorVM · HistoryVM
Services/     IDialogService · IThemeService · ILocalizationService
Converters/   冲突状态→颜色、字节大小→可读文本
```

---

## 7. 项目目录结构

```
FileManage/
├── FileManage.sln
├── docs/DESIGN.md
├── src/
│   ├── FileManage.Core/               # 纯逻辑库（无 UI 依赖）
│   │   ├── Scanning/    FileScanner.cs, ScanOptions.cs
│   │   ├── Naming/      NameEngine.cs, NamingOptions.cs, ReplaceStep.cs
│   │   ├── Rules/       RuleEngine.cs, ClassificationRule.cs
│   │   ├── Planning/    OperationPlanner.cs, ConflictDetector.cs
│   │   ├── Execution/   TransactionExecutor.cs, OverwritePolicy.cs
│   │   ├── Undo/        UndoStore.cs, UndoBatch.cs
│   │   └── Abstractions/ IFileSystemService.cs, IExifService.cs
│   ├── FileManage.Infrastructure/     # IO 实现
│   │   ├── JsonConfigStore.cs · ExcelExporter.cs · ExifService.cs
│   │   ├── FileBackupService.cs · SeriLogLogger.cs
│   ├── FileManage.App/                # WPF 入口
│       ├── Views/ · ViewModels/ · Services/ · Themes/
│       └── app.manifest (DPI aware)
├── tests/
│   └── FileManage.Core.Tests/         # xUnit
│       ├── NameEngineTests.cs · RuleEngineTests.cs
│       ├── ConflictDetectorTests.cs · TransactionExecutorTests.cs(临时目录)
└── .github/workflows/ci.yml
```

---

## 8. 测试策略

| 层级 | 范围 | 方式 |
|---|---|---|
| 单元 | NameEngine：全部变量 × 替换链 × 后缀保持组合用例；非法字符、超长名 | xUnit 参数化 |
| 单元 | RuleEngine：优先级、首条命中、条件组合、disabled 跳过 | xUnit |
| 单元 | ConflictDetector：4 类冲突的判定与自动改号 | xUnit |
| 集成 | TransactionExecutor：临时目录真实读写——成功/中途失败回滚/覆盖策略/取消 | xUnit + TempDir |
| 集成 | UndoStore：执行 → 撤销 → 目录状态与执行前逐字节一致 | xUnit |
| 手工 | UI 全流程冒烟清单（10 条主路径） | 发布前勾选 |

**黄金用例（回归基线）**：用旧版 PS 工具生成一批"输入→期望输出"快照目录，新版 Core 测试直接跑同一数据，保证对旧版行为完全兼容。

---

## 9. 里程碑计划

| 阶段 | 内容 | 完成标志 |
|---|---|---|
| **M0 骨架** | 解决方案 + 四个项目 + CI 跑通 | `dotnet build/test` 绿 |
| **M1 核心引擎** | Scanner / NameEngine / RuleEngine / ConflictDetector + 单测 | 测试全绿，黄金用例通过 |
| **M2 执行与撤销** | TransactionExecutor（事务/回滚）+ UndoStore + 集成测试 | 集成测试全绿 |
| **M3 基础 UI** | 主窗口三区布局、预览表格、执行/进度/取消、覆盖对话框 | 可完成"重命名+分类"全流程 |
| **M4 对齐旧版** | Excel 报表、config 持久化+快照、结果/错误日志、一键撤销 | 旧版功能清单逐项核对通过 |
| **M5 增强** | 替换链/正则、规则管理窗口+导入导出、EXIF 照片归档、去重、多级撤销 | 功能验收 |
| **M6 打磨发布** | 深色模式、双语、自包含单文件发布、README | v1.0 安装包产出 |

**依赖关系**：M1 → M2 → M3 → M4 严格串行；M5 内部各功能相互独立可并行；M6 收尾。

---

## 10. 风险与对策

| 风险 | 对策 |
|---|---|
| 长路径 (>260) 与 OneDrive 占位文件 | 启用长路径 API（`\\?\` 前缀）；跳过云占位符并提示 |
| 文件被占用（Excel/图片查看器） | 执行前逐个 `FileShare.ReadWrite` 试探，失败项跳过并记录 |
| 大目录预览卡顿 | 预览计算放后台 + 虚拟化 DataGrid，>5000 行时提示缩小范围 |
| EXIF 缺失 | 回退文件修改时间，预览中标注"回退" |
| 中文/emoji 文件名 | 全程 `string`（UTF-16），JSON 存储加 BOM 兼容 |
