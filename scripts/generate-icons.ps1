# 生成 SVG 与 PNG 图标资源
# 从内嵌的 Geometry 数据生成 assets/icons/ 下的 SVG 源与多尺寸 PNG（16/24/32/48）
# Path Data 与 src/filemanage.app/Themes/Icons.xaml 严格一致，可双向追溯
# 使用：powershell -NoProfile -ExecutionPolicy Bypass -File scripts/generate-icons.ps1
# 依赖：.NET WPF（PresentationCore/PresentationFramework）用于 PNG 渲染

param(
    [string]$OutDir = (Join-Path $PSScriptRoot '..\assets\icons')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore
Add-Type -AssemblyName PresentationFramework
Add-Type -AssemblyName WindowsBase

# ========== 图标定义（与 Icons.xaml Geometry 严格一致，可追溯） ==========
$icons = [ordered]@{
    'refresh'      = 'M21 12a9 9 0 1 1-2.64-6.36M21 4v5h-5'
    'execute'      = 'M5 3l14 9-14 9V3z'
    'undo'         = 'M9 14L4 9l5-5M4 9h11a5 5 0 0 1 0 10h-4'
    'history'      = 'M12 8v5l3 2M3 12a9 9 0 1 0 18 0 9 9 0 0 0-18 0z'
    'duplicate'    = 'M9 9h10v10H9zM4 14V4h10'
    'rule-editor'  = 'M4 6h16M4 12h16M4 18h10M5 6v12M5 6V4h14v2'
    'browse'       = 'M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7z'
    'appearance'   = 'M12 2v6m0 8v6M2 12h6m8 0h6M5 5l4 4m6 6l4 4M19 5l-4 4m-6 6l-4 4'
    'language'     = 'M3 5h12M9 3v2c0 5-3 9-6 10M5 9c2 4 6 6 9 6M14 21l5-11 5 11M16.5 17h5'
    'tools'        = 'M14 7a4 4 0 1 1-8 0M3 21l6-6M21 21l-5-5M9 3h8l4 4-4 4-4-4V3z'
    'help'         = 'M9 9a3 3 0 1 1 4 3c-1 1-1 2-1 3M12 17h.01M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18z'
    'about'        = 'M12 8h.01M11 12h1v4h1M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18z'
    'homepage'     = 'M14 3h7v7M21 3l-9 9M10 14L3 21M3 14v7h7'
    'guide'        = 'M4 4h12a2 2 0 0 1 2 2v14H6a2 2 0 0 1-2-2V4zM8 8h6M8 12h6M8 16h4'
    'faq'          = 'M21 11.5a8.5 8.5 0 1 1-17 0 8.5 8.5 0 0 1 17 0zM10 9a2 2 0 1 1 3 1.5c-.8.6-1 1-1 1.5M12 16h.01'
    'add'          = 'M12 5v14M5 12h14'
    'delete'       = 'M4 7h16M9 7V4h6v3M6 7l1 13h10l1-13M10 11v6M14 11v6'
    'up'           = 'M12 19V5M5 12l7-7 7 7'
    'down'         = 'M12 5v14M5 12l7 7 7-7'
    'save'         = 'M5 3h12l4 4v14H3V3zM8 3v5h8M8 13h8v8H8z'
    'close'        = 'M6 6l12 12M6 18L18 6'
    'import'       = 'M12 3v12M7 8l5 5 5-5M4 17v2h16v-2'
    'export'       = 'M12 3v12M7 10l5-5 5 5M4 17v2h16v-2'
    'copy'         = 'M9 9h10v10H9zM4 14V4h10v2'
    'rename'       = 'M4 20h4l10-10-4-4L4 16v4zM14 6l4 4'
    'source'       = 'M3 7a2 2 0 0 1 2-2h4l2 2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2V7zM3 11h18'
    'rename-group' = 'M3 9l4-4h4l8 8-4 4-8-8V9zM7 5l4 4'
    'classify'     = 'M3 7l4-4h4l8 8-4 4-8-8V7zM7 3l4 4M9 17h12'
    'exec-options' = 'M12 8v4l3 2M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18z'
    'warning'      = 'M12 3l10 18H2L12 3zM12 9v4M12 17h.01'
    'success'      = 'M20 6L9 17l-5-5'
    'info'         = 'M12 8h.01M11 12h1v4h1M12 3a9 9 0 1 0 0 18 9 9 0 0 0 0-18z'
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$sizes = 16, 24, 32, 48
$utf8Bom = New-Object System.Text.UTF8Encoding $true

# ========== 渲染 PNG：通过 WPF DrawingVisual + Geometry（保持矢量精度） ==========
function Render-Png([string]$pathData, [int]$size, [string]$outPath) {
    $g = [System.Windows.Media.Geometry]::Parse($pathData)
    $dv = New-Object System.Windows.Media.DrawingVisual
    $dc = $dv.RenderOpen()
    $pen = New-Object System.Windows.Media.Pen ([System.Windows.Media.Brushes]::Black, 1.5)
    $pen.StartLineCap = [System.Windows.Media.PenLineCap]::Round
    $pen.EndLineCap = [System.Windows.Media.PenLineCap]::Round
    $pen.LineJoin = [System.Windows.Media.PenLineJoin]::Round
    $scale = $size / 24.0
    $dc.PushTransform([System.Windows.Media.ScaleTransform]::new($scale, $scale))
    $dc.DrawGeometry($null, $pen, $g)
    $dc.Pop()
    $dc.Close()
    $bmp = [System.Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [System.Windows.Media.PixelFormats]::Pbgra32)
    $bmp.Render($dv)
    $enc = New-Object System.Windows.Media.Imaging.PngBitmapEncoder
    $enc.Frames.Add([System.Windows.Media.Imaging.BitmapFrame]::Create($bmp))
    $fs = [System.IO.File]::Create($outPath)
    $enc.Save($fs)
    $fs.Close()
}

# ========== 生成 SVG（24x24 viewBox，可编辑源） ==========
foreach ($name in $icons.Keys) {
    $d = $icons[$name]
    $svg = @"
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5" stroke-linecap="round" stroke-linejoin="round">
  <path d="$d" />
</svg>
"@
    [System.IO.File]::WriteAllText((Join-Path $OutDir "$name.svg"), $svg, $utf8Bom)
}
"已生成 $($icons.Count) 个 SVG 源文件至 $OutDir"

# ========== 渲染 PNG 多尺寸 ==========
foreach ($name in $icons.Keys) {
    $d = $icons[$name]
    foreach ($size in $sizes) {
        Render-Png $d $size (Join-Path $OutDir "${name}-${size}.png")
    }
}
"已生成 $($icons.Count * $sizes.Count) 个 PNG（$($sizes -join 'x, ')x）"

# ========== 索引 README ==========
$readme = Join-Path $OutDir 'README.md'
$iconList = $icons.Keys | ForEach-Object { "- $_" }
$readmeContent = @"
# FileManage 图标资源

- 源：`*.svg`（24x24 viewBox，stroke 1.5 round，currentColor，可编辑）
- 渲染：`{name}-16.png` / `{name}-24.png` / `{name}-32.png` / `{name}-48.png`（黑色描边，透明背景）
- WPF 集成：`src/filemanage.app/Themes/Icons.xaml`（Geometry + AppIcon/AppIcon16/20/24/32/48 样式，矢量自动支持任意尺寸与高 DPI）
- 路径数据一一对应，可通过 grep Geometry x:Key 与 SVG path d 双向追溯

## 图标列表（$($icons.Count) 个）
$($iconList -join "`n")
"@
[System.IO.File]::WriteAllText($readme, $readmeContent, $utf8Bom)
"已生成索引 README 至 $readme"
"完成。"
