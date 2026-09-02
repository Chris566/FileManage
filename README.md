# FileManage

批量文件**重命名**与**分类整理**工具（WPF / .NET 8）。从 PowerShell 脚本 `FileRenameTool.ps1` 演进而来的桌面版：
- **三层架构**：核心纯逻辑 / IO 基础设施 / WPF UI，核心零 IO 可单元测试
- **计划先行**：预览每一行的变更（重命名后文件名 / 分类目标路径 / 冲突类型高亮），确认后再执行
- **事务执行 + 完整可撤销**：备份 → 执行 → 提交/回滚三阶段；一键撤销或历史批次回滚
- **规则配置化**：多套预设、图形化规则编辑、优先级调序、JSON 导入导出
- **回归有保障**：432+12 个旧版黄金快照锁死核心命名 / 分类行为，155 个单元测试全量回归

---

## 功能总览

| 模块 | 说明 |
|---|---|
| 📁 **源目录扫描预览** | 选择 / 拖放源目录 → 即时扫描，预览表格显示原文件名 → 新文件名 → 分类 → 目标路径 → 冲突；右键「清理预览信息」可清空 |
| ✏️ **批量重命名** | 模板驱动（`{Index}` `{Origin}` `{Date}` `{ExifDate}` `{ExifYear}` `{Category}` 等 11 种占位符）+ 前缀 + 后缀保留 + 多步替换链（字面 / 正则 / 大小写 / 去空格 / 去字符） |
| 🗂️ **分类整理** | 规则支持扩展名 / 文件名正则 / 大小区间 / 日期区间的 **AND 组合**，按优先级首条命中，目标子目录模板支持 `{Category}` `{ExifYear}` `{yyyyMM}` 等；默认复制（安全）或移动，可选生成 Excel 分类报表 |
| 📋 **扫描导出报表** | 不执行操作，仅按规则预览分析 → 输出 `{源文件夹}-扫描导出报表-{时间}.xlsx` 到分类目标目录（格式与「分类整理报表」一致，方便离线核对或交给下游同事） |
| ↩️ **文件回覆** | 用户基于分类报表修改分类位置文件后，按报表中的路径映射自动覆盖回原始位置；遵循执行选项（询问 / 覆盖 / 跳过）；完成后弹窗可选择性清理：删除已分类文件 / 删除报表，并生成细粒度操作日志（时间 + 操作员 + 成功跳过失败统计） |
| 📸 **EXIF 照片归档** | 自动读取拍摄时间，按 `照片/{ExifYear}` 等结构归档；缺失 EXIF 回退修改时间 |
| ⚠️ **冲突检测** | 4 类：计划重名（自动改号 / 蓝色高亮） / 目标已存在（黄色） / 路径超长（红色） / 非法字符（红色） |
| 💾 **三阶段事务执行** | 备份 → 执行 → 提交/回滚；任何阶段异常自动回滚，绝不留半成品 |
| ↪️ **完整可撤销** | 撤销上次 / 历史批次多级撤销；撤销与报表删除**原子化**（要么都成功，要么都不做） |
| 🧹 **重复检测** | 两阶段 SHA-256 精确匹配（大小粗分 + 内容确认），勾选移回收站 |
| 📐 **规则管理** | 图形化增删改 / 上下移调优先级 / JSON 导入导出 |
| 🎯 **规则预设** | 多套规则预设下拉切换，切换即生效；系统默认「默认规则」完全锁定（只读保护）；自定义预设支持新建 / 复制 / 重命名 / 删除 |
| 🖱️ **拖放文件夹** | 直接拖文件夹到「源目录区 / 目标目录区 / 窗口任意位置」→ 悬停 Accent 高亮边框 + 提示文案；支持多文件夹（取第一个）、自动检测无权限 / 非文件夹场景并提示 |
| 👁️ **功能分组状态视觉** | 重命名 / 分类整理未启用时，整个分组 Opacity 淡化（0.25s 缓动动画过渡），启用态与主界面统一分隔风格 |
| 🏠 **启动自动预览 + 目录变更自动刷新** | 便捷版体验：上次目录有效 → 启动 200ms 后自动加载预览；修改源/目标路径时自动联动刷新 |
| ⌨️ **菜单栏 + 快捷键** | 外观（浅色/深色）、语言（中文/English）、工具（规则/重复/历史/刷新/执行/撤销）、帮助（用户指南/常见问题/检查更新/项目主页/关于）；F5 / Ctrl+E / Ctrl+Z / Ctrl+R / Ctrl+D / Ctrl+H / F1 |
| 🧠 **界面记忆** | 分组折叠状态 + 窗口位置/尺寸/最大化状态，持久化自动恢复 |
| ✨ **自定义应用图标** | 蓝色渐变文件夹图标（6 种尺寸 16/32/48/64/128/256），在桌面 / 任务栏 / 文件资源管理器清晰显示 |
| 🚀 **应用内自动更新** | 启动后台检测新版本 → 更新对话框（版本对比 + 更新日志 + 下载进度条 + 失败回滚）→ 按 manifest 清单增量安装 / 跨版本残留清理 / Data 用户数据全程排除 / 更新失败自动回滚 |

---

## 快速上手

```
1. 选择源目录：点击「浏览…」或把文件夹拖到窗口
2. 按需启用重命名 / 分类整理（未启用分组淡化显示，避免误操作）
   · 重命名：调整模板 / 前缀 / 替换链（照片可选「读取 EXIF 拍摄时间」）
   · 分类整理：启用 → 选择目标目录 →「规则管理」增删 / 调整优先级 → 复制 or 移动 → 是否生成报表
3. 预览表格自动刷新 → 逐行核对（冲突按颜色高亮）
4. 执行选项：目标同名「每次询问 / 全部覆盖 / 全部跳过」
5. 执行 → 查看状态栏进度；误操作可「撤销上次」或打开「历史…」撤销任何批次
6. F1 随时呼出帮助窗口（用户指南 / 常见问题 / 更新日志 / 检查更新）
```

---

## 构建 / 发布

依赖 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。NativeAOT 启动器编译额外需要**VS 2022 C++ 桌面开发工作负载**（Windows SDK + MSVC），Windows 最新版 CI 镜像自带。

```powershell
dotnet build -c Release      # 构建
dotnet test  -c Release      # 155 个单元测试（含 432+12 旧版黄金快照回归）
```

### 本地便携发布

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish.ps1
# 步骤：
#   · 从所有 git tag 重建 CHANGELOG.md（内嵌二进制的更新日志与 Release Notes 同源）
#   · 主程序 self-contained 发布 → 移入 runtime\
#   · NativeAOT 启动器 → 根目录 FileManage.exe
#   · 生成 manifest.json → 包装层 zip-stage/FileManage/
#   · 输出：FileManage.zip（解压即得 FileManage\）
```

### CI 发布

推送 `vX.Y.Z` 标签自动触发 `.github/workflows/ci.yml`：
1. **build-test**：Release 构建 + 155 单元测试
2. **publish**：打包脚本相同逻辑 → **启动冒烟测试**（启动器存活 10 秒验证，防止坏启动器进 Release）→ `FileManage.zip`
3. **release**：创建 GitHub Release，`FileManage.zip` 作为资产，**Release Notes 直接取自打标签时写入的附注消息**（与二进制内嵌 changelog 严格同源，避免不同步）

应用内更新遵循 `manifest.json`：**增量复制有变化的文件** + 删除新版中不存在的旧文件（跨版本残留清理）+ 失败从 `_update_backup` 自动回滚。

---

## 目录布局与数据文件

### 发布包（解压后 `FileManage\`）

```
FileManage\
├── FileManage.exe            唯一根目录文件（NativeAOT 启动器，应用图标；hostfxr 从 runtime\ 加载运行时）
├── manifest.json             版本清单：文件相对路径 + SHA256 + 大小，供增量更新
├── runtime\                  .NET 8 运行时 / WPF 框架 / 第三方依赖 / FileManage.dll（由 dotnet publish 生成，不要手工挪）
├── Data\                     ↓ 用户数据，更新时全程排除、永不覆盖
│   ├── rules.json            分类规则预设（v2：多预设 + 激活项；旧版 v1 首次启动自动无损迁移）
│   ├── settings.json         主题 / 语言 / 上次目录 / 分组折叠 / 窗口位置
│   ├── undo\                 撤销批次 JSON（撤销上次 / 历史依赖）
│   └── backup\               执行前备份（撤销事务依赖）
└── _update_backup\           更新失败回滚用（发布包中不存在；更新成功后下次启动自动清理）
```

> 便携版数据跟随文件夹走（U 盘携带、跨机器拷贝即生效）。若 exe 被安装到无写权限的 `Program Files`，自动回退 `%AppData%\FileManage\Portable`。

### 项目源代码结构

```
src/
├── FileManage.Core/             纯逻辑（零 IO，可测试）
│   ├── Naming/  Rules/  Planning/
│   ├── Execution/  Reporting/  Undo/  Duplicate/  Scanning/
│   └── Abstractions/            Core 所需的接口（IFileSystem 等）
├── FileManage.Infrastructure/   IO 实现
│   ├── FileSystem/  Exif/  Backup/
│   ├── Undo/  Rules/  Settings/  Storage/  Reporting/
│   └── UpdateManifest.cs        版本清单模型 + 对比器
├── FileManage.App/              WPF UI（MVVM + CommunityToolkit.Mvvm）
│   ├── Services/                路径（AppPaths）/ 更新 / Changelog 加载等
│   ├── ViewModels/              MainViewModel / RuleEditorViewModel / HistoryViewModel…
│   ├── Views/                   所有窗口（Main / RuleEditor / History / Update / Help / Changelog…）
│   ├── Converters/              ZeroToVisible / IValueConverter 扩展
│   ├── Localization/            zh-CN.xaml + en-US.xaml（即时切换）
│   └── Themes/                  Light.xaml + Dark.xaml + Controls.xaml（控件模板 + 34+ 矢量图标）
└── FileManage.Launcher/         NativeAOT 启动器（hostfxr 从 runtime\ 加载运行时，托管代码零侵入）

tests/FileManage.Core.Tests/     xUnit + 155 用例（Core / Infra 均覆盖；含 legacy 黄金快照）
docs/                            设计 / 计划文档（DESIGN.md / RULE_PRESET_PLAN.md / 测试报告等）
tools/                           旧版 PS 工具黄金快照生成脚本（留作基线）
scripts/publish.ps1              便携版发布脚本（与 CI 完全一致）
.github/workflows/ci.yml         CI：构建-测试-发布-Release 四阶段
CHANGELOG.md                     每次 CI 打包前从 git tag 自动重建，二进制内嵌与 Release Notes 同源
```

---

## 文件分类标准

### 新增文件归放规则

| 文件类型 | 放置位置 |
|---|---|
| 纯业务逻辑（无 IO 依赖） | `src/FileManage.Core` 对应子目录（Naming / Rules / Planning / Execution / Reporting / Undo / Duplicate / Scanning） |
| 文件系统 / 持久化 / 外部服务实现 | `src/FileManage.Infrastructure` 对应子目录 |
| UI 层服务 / 路径 / 本地更新 / Changelog 加载 | `src/FileManage.App/Services` |
| 窗口 XAML + code-behind | `src/FileManage.App/Views` |
| 视图模型 | `src/FileManage.App/ViewModels` |
| 值转换器 | `src/FileManage.App/Converters` |
| 主题 / 本地化资源字典 | `src/FileManage.App/Themes` 或 `src/FileManage.App/Localization` |
| 测试 | `tests/FileManage.Core.Tests`（Core / Infra 均可测） |
| 发布 / 构建脚本 | `scripts/`；CI 工作流 `.github/workflows/` |
| 设计 / 测试报告 / 计划文档 | `docs/`；面向最终用户的功能说明写入 `README.md` |

> **发布包新增根目录文件**：必须同步更新 `ci.yml` + `scripts/publish.ps1` 的 manifest 生成与编排逻辑；否则增量更新在跨版本时可能无法正确迁移。

### 更新机制（增量 / 跨版本）

1. **检测**：启动后台请求 GitHub Release → 语义版本号比较；用户可从「帮助 → 检查更新…」手动触发
2. **下载**：用户在更新对话框点「下载并安装」→ 带进度下载 zip
3. **安装**（批处理，等待进程退出后执行）：
   - 程序文件备份到 `_update_backup`（排除 `Data\`）
   - 自动识别 zip 内包装层（`FileManage\` wrapper）
   - robocopy 覆盖，天然仅复制变化的文件（**增量安装**）
   - 按 manifest 对比删除新版中不存在的残留（**跨版本清理**）
   - 启动新版本
4. **回滚**：任一步失败自动从备份恢复；新进程启动后若成功会清理 `_update_backup`

---

## 与旧版 PS 工具的行为一致

黄金快照（`tests/FileManage.Core.Tests/TestData/legacy-golden-snapshot.json`）由旧版 PowerShell 工具生成（`tools/generate-golden-snapshot.ps1`），锁定 432 个命名用例 + 12 个分类用例的行为基线。任何核心引擎改动都会被该测试校验，保证与旧版行为一致；已知差异在快照 `knownDifferences` 字段中**显式声明**。

---

## License

MIT
