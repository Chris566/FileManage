# 规则预设系统升级方案（规则管理 / 分类整理）

状态：已实施（v1.6.0，测试报告见 docs/RULE_PRESET_TEST_REPORT.md）
日期：2026-09-01
关联：docs/DESIGN.md §4.3（分类规则）、规则管理现状（RuleEditorWindow / RuleConfigStore / rules.json v1）

## 一、目标与范围

在"分类整理 → 规则管理"中引入**预设（Preset）**体系：

- 现有规则配置平滑迁移为**系统默认预设**（不丢失任何规则）
- 系统默认预设**完全锁定**：可查看、可复制，禁止编辑内容、禁止重命名、禁止删除
- 用户可**创建 / 复制 / 重命名 / 编辑 / 删除**自定义预设，支持自定义命名
- 预设**切换即生效**：下拉选中即写盘，主窗口下一次预览/执行即使用新预设
- 多预设数据相互独立，切换后一致

## 二、已确认的设计决策

| 决策点 | 结论 |
|---|---|
| 存储 | `rules.json` 升级 v2，单文件容纳全部预设 + 激活项 |
| 默认预设权限 | 完全锁定（内容只读，仅可查看/复制/导出） |
| 切换语义 | 切换即生效（选中即保存激活项并写盘；切换前自动保存原预设的编辑修改，防丢失） |

## 三、数据结构变更（rules.json v1 → v2）

v1（现状）：

```json
{ "Version": 1, "Rules": [ { "Id": "...", "Name": "图片", ... } ] }
```

v2（本方案）：

```json
{
  "Version": 2,
  "ActivePresetId": "0f7e...",
  "Presets": [
    { "Id": "0f7e...", "Name": "默认规则", "IsBuiltIn": true,  "Rules": [ ...迁移自 v1 的全部规则，原样保留... ] },
    { "Id": "8a1c...", "Name": "照片归档", "IsBuiltIn": false, "Rules": [ ... ] }
  ]
}
```

- `IsBuiltIn=true` 的预设由迁移/内置生成，权限控制以其为准（UI 标识 + 逻辑双重保护）
- 导入/导出格式不变（仍是 v1 单规则集 JSON）：导出=当前预设规则集，导入=自动创建新的自定义预设（以文件名命名，避免误覆盖）
- UTF-8 BOM、缩进等序列化习惯与现状一致

### 迁移规则（保证不丢失）

| 文件状态 | 行为 |
|---|---|
| 不存在 | 用内置默认 6 条规则创建"默认规则"预设（IsBuiltIn=true）并写盘 |
| v1 格式 | 全部规则原样包装为"默认规则"预设（IsBuiltIn=true），写回 v2 —— 现有配置成为系统默认规则 |
| v2 格式 | 直接加载 |
| 损坏 | 回退内置默认集（不覆盖坏文件，便于手工恢复），与现状一致 |

## 四、分层设计（对齐三层架构，Core 零 IO）

### Core（纯逻辑，可单测）

- `RulePreset`：`Id / Name / IsBuiltIn / Rules`
- `RulePresetDocument`：`Version=2 / ActivePresetId / Presets`
- `RulePresetManager`：纯内存状态机，所有变更产出新 `RulePresetDocument`
  - `MigrateFromRules(rules)`：v1 → v2（静态纯函数）
  - `SwitchPreset(id)` / `ActivePreset` / `ActiveRules`
  - `CreatePreset(name, rules)`（重名拒绝）
  - `CopyPreset(sourceId, newName)`：新 Id、IsBuiltIn=false、规则深拷贝
  - `RenamePreset(id, name)`：内置拒绝
  - `DeletePreset(id)`：内置拒绝；删除激活项后激活落到剩余首个预设
  - `UpdateRules(id, rules)`：内置拒绝（完全锁定）
  - 操作结果 `PresetResult(bool Success, PresetError? Error)`，错误文案在 UI 层本地化

### Infrastructure（IO）

- `RulePresetStore`：
  - `LoadOrMigrate(path, fallbackDefaults)` → `RulePresetDocument`（覆盖上表四种文件状态）
  - `Save(path, document)`（v2 写盘）
  - `LoadLegacyRules(path)` → v1 单规则集（导入用，复用 `RuleConfigStore`）
  - `SaveLegacyRules(path, rules)`（导出用）

### App（WPF / MVVM）

- `AppServices`：`LoadRules()` 改为返回**激活预设的规则**（签名不变，`MainViewModel.BuildRules()` 与规则编辑器零改动）
- `RuleEditorViewModel`：
  - 预设集合 `Presets`（`PresetItem` 包装：DisplayName = `默认规则（系统）` / `名字`）
  - `SelectedPreset` 变化 → 自动保存原预设（若有修改）→ `SwitchPreset` → 重载规则列表 → 写盘
  - `CanEditRules = !ActivePreset.IsBuiltIn`：联动禁用 新增/删除/上下移/编辑面板/导入/保存
  - 命令：`NewPreset / CopyPreset / RenamePreset / DeletePreset`（删除内置 → 明确提示"系统默认预设受保护，不可删除"）
  - `Save`：编辑列表写回激活预设并写盘；`Export` 导出激活预设规则集
- `RuleEditorWindow.xaml`：顶部新增预设条（ComboBox + 新建/复制/重命名/删除），锁定态给出"系统默认预设（只读）"徽标；界面文案走 `S.Preset.*` 本地化（中/英即时切换）

## 五、交互设计

- 区别标识：ComboBox 内置项显示"默认规则（系统）"，自定义项显示原名；预设条右侧常驻状态徽标（内置=只读锁标，自定义=可编辑）
- 删除保护：对内置预设，删除按钮直接禁用；若通过其他途径触发（如双击/命令），弹提示明确告知不可删除
- 快速切换：ComboBox 单击即切换生效，无需保存步骤；主窗口预览自动反映（复用现有"关闭规则窗口后刷新预览"链路）
- 重命名：就地对话框输入，重名校验（与其他预设名冲突时提示）

## 六、里程碑

| 阶段 | 内容 | 交付 |
|---|---|---|
| P1 | Core：模型 + Manager + 迁移函数 + 单测（~12 例） | 纯逻辑全绿 |
| P2 | Infrastructure：RulePresetStore（v1/v2/损坏/往返）+ 单测（~6 例） | 持久化全绿 |
| P3 | App：AppServices / RuleEditorViewModel / UI / 本地化 + UI 自动化验证 | 界面可交付 |
| P4 | 文档：方案存档、测试报告、帮助窗口指南/FAQ、README、CHANGELOG | 交付物齐备 |

## 七、测试矩阵（对应需求 §5）

1. 默认预设不可删除（Manager 层拒绝 + UI 提示）
2. 自定义预设创建 / 复制 / 重命名 / 删除 / 编辑
3. 切换生效：切换后 `LoadRules()` 返回新激活预设规则；主窗口预览行为随之变化
4. 多预设一致性：A/B 预设规则互不影响；切换往返后规则逐条相等
5. 迁移：v1 文件 → 默认预设规则无损；旧设置场景回归（无文件 / 损坏）
6. 全量回归：现有 119 项测试不回归

## 八、验收标准

1. 老用户升级后打开规则管理：看到"默认规则（系统）"预设，内容与升级前完全一致
2. 默认预设无法被删除或修改（含 UI 与逻辑层）
3. 新建/复制预设 → 编辑 → 切换 → 主窗口预览按新规则即时变化
4. 中/英界面、浅/深主题下预设区显示正常
5. 导出 v2 中的预设 → 得到 v1 兼容 JSON，可被导入重建
