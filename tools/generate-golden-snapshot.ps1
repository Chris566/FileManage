# ============================================================
# 黄金快照生成器（设计文档 §8 兼容性保障）
# 从旧版 FileRenameTool.ps1 提取核心纯函数（Build-RenameName 等），
# 构造输入矩阵并调用旧版逻辑，生成"输入 -> 期望输出"JSON，
# 供 tests/FileManage.Core.Tests/LegacyGoldenSnapshotTests 作回归基线。
# 重新生成：powershell -NoProfile -ExecutionPolicy Bypass -File tools/generate-golden-snapshot.ps1
# ============================================================

param(
    [string]$SourceScript = "D:\01_product\FileRenameTool\FileRenameTool.ps1",
    [string]$OutputPath
)

$ErrorActionPreference = "Stop"

if (-not $OutputPath) {
    $OutputPath = Join-Path $PSScriptRoot "..\tests\FileManage.Core.Tests\TestData\legacy-golden-snapshot.json"
}

if (-not (Test-Path -LiteralPath $SourceScript)) {
    throw "未找到旧版脚本: $SourceScript"
}

$text = Get-Content -LiteralPath $SourceScript -Raw -Encoding UTF8

# ---- 平衡括号提取文本块 ----
function Extract-BraceBlock {
    param([string]$Text, [string]$Anchor)

    $start = $Text.IndexOf($Anchor)
    if ($start -lt 0) { throw "未找到锚点: $Anchor" }

    $openIndex = $Text.IndexOf("{", $start)
    if ($openIndex -lt 0) { throw "锚点后未找到起始括号: $Anchor" }

    $depth = 0
    for ($i = $openIndex; $i -lt $Text.Length; $i++) {
        if ($Text[$i] -eq "{") { $depth++ }
        elseif ($Text[$i] -eq "}") {
            $depth--
            if ($depth -eq 0) {
                return $Text.Substring($start, $i - $start + 1)
            }
        }
    }

    throw "大括号不平衡: $Anchor"
}

# ---- 提取旧版核心函数与数据 ----
$funcGetTemplate = Extract-BraceBlock -Text $text -Anchor "function Get-NormalizedRenameTemplate"
$funcBuildName   = Extract-BraceBlock -Text $text -Anchor "function Build-RenameName"

$anchorGroups = '$script:FileTypeGroups = @('
$startGroups = $text.IndexOf($anchorGroups)
if ($startGroups -lt 0) { throw "未找到 FileTypeGroups 定义" }

$depth = 0
$blockGroups = $null
for ($i = $startGroups; $i -lt $text.Length; $i++) {
    if ($text[$i] -eq "(") { $depth++ }
    elseif ($text[$i] -eq ")") {
        $depth--
        if ($depth -eq 0) {
            $blockGroups = $text.Substring($startGroups, $i - $startGroups + 1).Replace('$script:', '$')
            break
        }
    }
}
if ($null -eq $blockGroups) { throw "FileTypeGroups 括号不平衡" }

$blockMap = Extract-BraceBlock -Text $text -Anchor '$renameTemplateMap = @{'

# 加载旧版函数与数据
Invoke-Expression $funcGetTemplate
Invoke-Expression $funcBuildName
Invoke-Expression $blockGroups
Invoke-Expression $blockMap

# ---- 构造命名用例矩阵 ----
$oldNames = @("报告.pdf", "会议纪要.docx", "IMG_2024.JPG", "README", "a.b.c.pdf", "带 空格 文件.txt")
$prefixes = @("", "合同_", "IMG_")
$indexes  = @(1, 2, 12)
$keeps    = @($true, $false)

$namingCases = @()
foreach ($n in $oldNames) {
    foreach ($t in $renameTemplateMap.Values) {
        foreach ($p in $prefixes) {
            foreach ($ix in $indexes) {
                foreach ($k in $keeps) {
                    $namingCases += [pscustomobject]@{
                        oldName               = $n
                        prefix                = $p
                        template              = $t
                        index                 = $ix
                        keepOriginalExtension = $k
                        expected              = Build-RenameName -OldName $n -Prefix $p -Template $t -Index $ix -KeepOriginalExtension $k
                    }
                }
            }
        }
    }
}

# ---- 分类映射（全选状态）----
$classificationCases = @()
foreach ($group in $fileTypeGroups) {
    foreach ($ext in $group.Extensions) {
        $classificationCases += [pscustomobject]@{
            extension        = $ext.ToLowerInvariant()
            expectedCategory = $group.Name
        }
    }
}

# ---- 输出 ----
$templateList = @()
foreach ($key in $renameTemplateMap.Keys) {
    $templateList += [pscustomobject]@{ name = $key; template = $renameTemplateMap[$key] }
}

$snapshot = [pscustomobject]@{
    generatedAt         = (Get-Date -Format "yyyy-MM-dd HH:mm:ss")
    source              = $SourceScript
    templates           = $templateList
    namingCases         = $namingCases
    classificationCases = $classificationCases
    knownDifferences    = @(
        "旧版: 文件名已有前缀(忽略大小写)时跳过重命名, 仅做分类复制; 新版在 M4 的 OperationPlanner 中对齐",
        "旧版: 重命名目标已存在 -> 标记失败并跳过; 新版: 冲突自动改号 _2/_3 (设计文档 4.4 有意增强)",
        "旧版: {Index} 仅对实际执行重命名的文件递增; 新版在 M4 的 OperationPlanner 中对齐",
        "旧版分类组: PDF/WORD/EXCEL/PPT/IMAGE; 新版内置规则集为增强版(合并 Office 并新增视频/音频/压缩包), 本快照用旧版组构造等价规则验证引擎语义"
    )
}

$dir = Split-Path -Parent $OutputPath
if (-not (Test-Path -LiteralPath $dir)) {
    New-Item -ItemType Directory -Path $dir -Force | Out-Null
}

$snapshot | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $OutputPath -Encoding UTF8

Write-Host "黄金快照已生成: $OutputPath"
Write-Host "命名用例: $($namingCases.Count), 分类用例: $($classificationCases.Count)"
