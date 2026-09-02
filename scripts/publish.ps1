# FileManage 发布脚本：self-contained 便携版（runtime\ 子目录 + NativeAOT 启动器），与 CI 发布逻辑一致
# 用法: powershell -ExecutionPolicy Bypass -File scripts/publish.ps1 [-SkipZip]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root "publish"
$runtimeDir = Join-Path $outDir "runtime"
$appPublish = Join-Path $root "publish-runtime"
$launcherPublish = Join-Path $root "publish-launcher"

# dotnet 解析：优先默认安装位置，避免命中 WindowsApps 存根
$dotnet = Join-Path $env:LOCALAPPDATA "Microsoft\dotnet\dotnet.exe"
if (-not (Test-Path $dotnet))
{
    $dotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
}

# 0) 每次打包前从所有 git tag 重建 CHANGELOG.md（最新版 tag 附注即为本版更新说明）
$changelog = @("# 更新日志 / Changelog", "")
$today = Get-Date -Format "yyyy-MM-dd"
$tags = git tag --sort=-v:refname 2>$null
if ($tags)
{
    foreach ($tag in $tags) {
        $date = git log -1 --format=%cs "$tag^{}" 2>$null
        if (-not $date) { $date = $today }
        $changelog += "", "## $tag ($date)", ""
        $body = git tag -l --format='%(contents)' $tag 2>$null
        if ($body) {
            $lines = @($body -split "`r?`n")
            if ($lines.Count -gt 1) {
                $changelog += ($lines[1..($lines.Count-1)] | Where-Object { $_.Trim().Length -gt 0 })
            }
        }
    }
}
else
{
    $changelog += "", "（本地开发版：未检出 git tag，发布产物使用 CI 时会自动写入完整更新日志）", ""
}
$changelogPath = Join-Path $root "CHANGELOG.md"
$changelog -join "`n" | Set-Content $changelogPath -Encoding UTF8
Write-Host "CHANGELOG.md 已重建（含 $($tags.Count) 个版本，嵌入二进制）" -ForegroundColor Cyan

# 1) 主程序 self-contained 发布
& $dotnet publish (Join-Path $root "src/FileManage.App/FileManage.App.csproj") `
    -c $Configuration -r $Runtime --self-contained true `
    -o $appPublish

if ($LASTEXITCODE -ne 0)
{
    Write-Host "主程序发布失败（dotnet publish 退出码 $LASTEXITCODE）" -ForegroundColor Red
    exit $LASTEXITCODE
}

# 2) 编排：重建 publish 根（清除旧版遗留），runtime\ 子目录承载全部运行时与依赖
if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
New-Item -ItemType Directory -Path $runtimeDir -Force | Out-Null
Move-Item (Join-Path $appPublish "*") $runtimeDir
Remove-Item (Join-Path $runtimeDir "FileManage.exe"), (Join-Path $runtimeDir "*.pdb") -Force
Remove-Item (Join-Path $runtimeDir "createdump.exe") -Force -ErrorAction SilentlyContinue

# 3) NativeAOT 启动器 → 根目录 FileManage.exe（hostfxr 从 runtime\ 加载运行时）
& $dotnet publish (Join-Path $root "src/FileManage.Launcher/FileManage.Launcher.csproj") `
    -c $Configuration -r $Runtime -o $launcherPublish

if ($LASTEXITCODE -ne 0)
{
    Write-Host "启动器发布失败（NativeAOT 需要 VS C++ 桌面开发工作负载）" -ForegroundColor Red
    exit $LASTEXITCODE
}

Copy-Item (Join-Path $launcherPublish "FileManage.Launcher.exe") (Join-Path $outDir "FileManage.exe") -Force

# 4) 生成版本清单（根目录相对路径），供应用内增量更新与跨版本残留清理
$files = Get-ChildItem $outDir -Recurse -File | ForEach-Object {
    @{
        path   = [IO.Path]::GetRelativePath($outDir, $_.FullName).Replace('\', '/')
        sha256 = (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        size   = $_.Length
    }
}
[ordered]@{
    version     = "local"
    generatedAt = (Get-Date).ToUniversalTime().ToString("o")
    files       = $files
} | ConvertTo-Json -Depth 4 | Set-Content (Join-Path $outDir "manifest.json") -Encoding UTF8

Write-Host ""
Write-Host "发布完成: $outDir\FileManage.exe（根目录仅启动器，运行时在 runtime\，数据写入 Data\）" -ForegroundColor Green

if (-not $SkipZip)
{
    # 压缩包结构：FileManage.zip 内仅一个名为 FileManage 的文件夹（解压后结构固定、无版本号）
    $stage = Join-Path $root "zip-stage"
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    New-Item -ItemType Directory -Path (Join-Path $stage "FileManage") | Out-Null
    Move-Item (Join-Path $outDir "*") (Join-Path $stage "FileManage")
    $zipPath = Join-Path $root "FileManage.zip"
    Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zipPath -Force
    Write-Host "打包完成: $zipPath（解压后得到 FileManage\）" -ForegroundColor Green
}
