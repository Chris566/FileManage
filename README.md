# FileManage

批量文件重命名与分类整理工具（WPF / .NET 8）。由 PowerShell 脚本 `FileRenameTool.ps1` 演进而来的桌面版：核心引擎与 IO 分层、计划先行（先预览后执行）、完整可撤销、规则可配置、回归有保障。

## 功能

- **批量重命名**：模板驱动（11 个变量：`{Index}`、`{Date}`、`{ExifDate}`、`{Origin}` 等）+ 前缀 + 可视化替换链（字面/正则/大小写/整理空格/移除字符）
- **分类整理**：按规则（扩展名、名称正则、大小区间、日期区间 AND 组合）移动或复制到目标子目录，支持 `{Category}`、`{ExifYear}` 等子目录模板
- **EXIF 照片归档**：读取拍摄时间，按 `照片/{ExifYear}` 等结构归档（缺失 EXIF 回退修改时间）
- **冲突检测**：计划重名（自动改号）、目标已存在、路径超长、非法字符四类，预览行按类型高亮
- **两阶段执行 + 完整可撤销**：备份 → 执行 → 提交/回滚三阶段事务；自动回滚（异常）与手动撤销（上次 / 历史批次多级撤销）
- **重复检测**：两阶段 SHA-256 精确匹配（大小粗分 + 内容确认），勾选移入回收站
- **规则管理**：图形化编辑、上下移调优先级、JSON 导入导出
- **规则预设**：多套分类规则预设一键切换（切换即生效）；现有规则自动无损迁移为"默认规则"系统预设并受只读保护（禁编辑/重命名/删除），自定义预设支持新建/复制/重命名/删除
- **菜单栏**：外观（浅色/深色）、语言（中文/English）、工具（规则管理/重复检测/历史/刷新/执行/撤销）、帮助（用户指南/常见问题/项目主页）单选与命令入口
- **全局快捷键**：F5 刷新预览、Ctrl+E 执行、Ctrl+Z 撤销上次、Ctrl+R 规则管理、Ctrl+D 重复检测、Ctrl+H 历史、F1 帮助
- **关于 / 更新日志**：右下角版本号悬停显示构建日期、点击打开"关于"窗口（版本/版权/许可/开发者/更新日志）
- **界面记忆**：分组折叠状态与窗口位置/尺寸/最大化状态自动持久化，重启后原样恢复
- **深色 / 浅色主题**、**中文 / English 界面**（菜单栏即时切换），设置（含上次目录）自动持久化

## 构建

依赖 [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)。

```powershell
dotnet build -c Release      # 构建
dotnet test -c Release       # 全量测试（含黄金快照回归 432+12 用例）
```

## 发布

自包含单文件（目标机器无需安装 .NET 运行时）：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/publish.ps1
# 产出: publish/FileManage.exe
```

CI（`.github/workflows/ci.yml`）在推送 `v*` 标签或手动触发时自动构建并上传 `FileManage.exe` 工件。

## 使用说明

1. 选择源目录（可包含子目录、配置包含/排除通配）
2. 启用重命名 / 分类整理，按需调整模板、前缀、替换规则、分类规则（分组可折叠，状态自动记忆）
3. **刷新预览** 检查每一行的目标名称与冲突标记
4. 选择"目标已存在"策略（询问 / 覆盖全部 / 跳过全部）后 **执行**
5. 误操作可 **撤销上次** 或从 **历史…** 撤销任意批次；分类默认复制模式更安全
6. 首次使用可按 **F1** 打开帮助窗口（用户指南 / 常见问题）

## 数据文件位置

| 文件 | 路径 | 说明 |
|---|---|---|
| rules.json | `%AppData%\FileManage\rules.json` | 分类规则预设（v2：多预设 + 激活项；旧版 v1 单规则集首次启动自动无损迁移，可导入导出） |
| settings.json | `%AppData%\FileManage\settings.json` | 主题、语言、上次目录、分组折叠、窗口位置/尺寸 |
| undo/*.json | `%AppData%\FileManage\undo\` | 撤销批次记录 |
| backup/ | `%AppData%\FileManage\backup\` | 执行前备份（撤销依赖） |

## 项目结构

```
src/
  FileManage.Core            纯逻辑：命名引擎、规则引擎、计划器、事务执行、撤销、去重
  FileManage.Infrastructure  IO 实现：文件系统、EXIF、备份、撤销存储、规则/设置持久化
  FileManage.App             WPF UI（MVVM，CommunityToolkit.Mvvm）
tests/FileManage.Core.Tests  xUnit 单元测试 + 旧版黄金快照回归
docs/DESIGN.md               架构与里程碑设计文档
tools/                       旧版 PS 工具黄金快照生成脚本
scripts/publish.ps1          自包含单文件发布脚本
```

## 与旧版的关系

黄金快照（`tests/FileManage.Core.Tests/TestData/legacy-golden-snapshot.json`）由旧版 PowerShell 工具生成（`tools/generate-golden-snapshot.ps1`），锁定 432 个命名用例 + 12 个分类用例的行为基线。任何核心引擎改动都会被该测试校验，保证与旧版行为一致（已知差异在快照 `knownDifferences` 中显式声明）。

## License

MIT
