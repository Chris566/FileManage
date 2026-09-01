# 规则预设系统 功能测试报告

日期：2026-09-01 ｜ 版本：v1.6.0 ｜ 关联计划：docs/RULE_PRESET_PLAN.md

## 一、测试环境

- Windows 11 ／ .NET 8 ／ Debug 构建
- 单元测试：xUnit（`tests/FileManage.Core.Tests`）
- 端到端验收：PowerShell + UIAutomation 驱动真实应用（脚本 `.tmp_preset_e2e.ps1`，验证后可删除）

## 二、单元测试（22 项新增，全量 141 项全部通过）

### 1. 迁移与加载（RulePresetManagerTests / RulePresetStoreTests）

| 用例 | 验证点 | 结果 |
|---|---|---|
| MigrateFromRules_PreservesAllRules_AsBuiltIn | v1 规则全部无损转为内置预设 | 通过 |
| MigrateFromRules_EmptyRules_CreatesEmptyBuiltIn | 空规则集迁移边界 | 通过 |
| Constructor_InvalidActiveId_FallsBackToFirstPreset | 激活项损坏时回退首个预设 | 通过 |
| LoadOrMigrate_NoFile_CreatesV2FromDefaults | 无文件 → 内置默认集创建 v2 | 通过 |
| LoadOrMigrate_V1File_MigratesLosslessAndWritesBackV2 | v1 文件无损迁移并写回 v2 | 通过 |
| LoadOrMigrate_V2File_LoadsDirectly | v2 直接加载（含中文名往返） | 通过 |
| LoadOrMigrate_CorruptFile_FallsBackWithoutOverwrite | 损坏文件回退默认集且不覆盖原文件 | 通过 |
| SaveLoad_RoundTrip_PreservesPresetsWithChineseNames | 多预设序列化往返一致 | 通过 |
| LegacyImportExport_V1Format_CompatibleWithRuleConfigStore | 导入/导出与 v1 格式互通 | 通过 |

### 2. 预设管理与权限控制

| 用例 | 验证点 | 结果 |
|---|---|---|
| SwitchPreset_Valid_UpdatesActiveRules | 切换后 ActiveRules 即时变化 | 通过 |
| SwitchPreset_UnknownId_Fails | 未知 Id 拒绝 | 通过 |
| CreatePreset_AddsCustomAndActivates | 新建自定义预设并激活 | 通过 |
| CreatePreset_DuplicateName_Fails | 重名拒绝 | 通过 |
| CreatePreset_BlankName_Fails | 空名拒绝 | 通过 |
| CopyPreset_IndependentCopy_WithNewId | 复制为新 Id 且规则深拷贝、相互独立 | 通过 |
| RenamePreset_BuiltIn_Fails | **默认预设禁止重命名** | 通过 |
| RenamePreset_Custom_Succeeds | 自定义预设可重命名 | 通过 |
| DeletePreset_BuiltIn_Fails | **默认预设禁止删除** | 通过 |
| DeletePreset_ActiveCustom_ReactivatesFirstRemaining | 删除激活预设后回落到剩余首个 | 通过 |
| UpdateRules_BuiltIn_Fails_CompletelyLocked | **默认预设内容完全锁定** | 通过 |
| UpdateRules_Custom_Succeeds | 自定义预设内容可更新 | 通过 |
| MultiPresets_SwitchingBackAndForth_RulesRemainIndependent | 多预设往返切换数据一致 | 通过 |

## 三、端到端 UI 自动化验收（真实应用）

前置：将 `%AppData%\FileManage\rules.json` 重置为 v1 旧格式（2 条规则：图片 / 文档），模拟老用户存量数据。

| 步骤 | 验证点 | 结果 |
|---|---|---|
| 1. 启动应用 | 启动即触发 v1→v2 迁移：Version=2、内置预设 IsBuiltIn=True、2 条规则无损 | PASS |
| 2. 打开规则管理 | 预设下拉显示"默认规则（系统）"标识；右侧"系统默认预设 · 只读"徽标显示；锁定态下"＋新增"按钮禁用 | PASS |
| 3. 新建预设 | 输入"测试预设A"→ 自动切换为激活项、显示"自定义预设 · 可编辑"徽标、编辑按钮恢复可用 | PASS |
| 4. 切回默认预设 | 键盘切换后恢复只读徽标 | PASS |
| 5. 删除保护 | 点击"删除"弹出"系统默认预设受保护，不可删除或修改…"提示（截图 .tmp/preset/3-delete-protected.png） | PASS |
| 6. 落盘校验 | 关闭后 rules.json：Version=2，含"默认规则(内置,2 规则)"与"测试预设A(自定义)"，数据完整 | PASS |

## 四、全量回归

```
dotnet test FileManage.sln -c Debug
已通过! - 失败: 0，通过: 141，已跳过: 0，总计: 141
```

- 原有 119 项测试（含旧版 PowerShell 黄金快照回归 432 命名用例 + 12 分类用例）无回归
- 新增 22 项预设测试全部通过

## 五、结论

需求 §5 测试矩阵全部满足：默认规则不可删除（逻辑层拒绝 + UI 弹窗双重防护）、自定义预设增删改查与重命名正常、预设切换即时生效并落盘、多预设数据相互独立一致、v1→v2 迁移无损。**验收通过。**
