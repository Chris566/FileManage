# FileManage 发布脚本：self-contained 便携文件夹 + zip（与 CI 发布逻辑一致）
# 用法: powershell -ExecutionPolicy Bypass -File scripts/publish.ps1 [-SkipZip]
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipZip
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root "publish"

dotnet publish (Join-Path $root "src/FileManage.App/FileManage.App.csproj") `
    -c $Configuration -r $Runtime --self-contained true `
    -o $outDir

if ($LASTEXITCODE -ne 0)
{
    Write-Host "发布失败（dotnet publish 退出码 $LASTEXITCODE）" -ForegroundColor Red
    exit $LASTEXITCODE
}

# 瘦身：排除调试符号与崩溃转储工具（最终用户无需）
Remove-Item (Join-Path $outDir "*.pdb") -Force
Remove-Item (Join-Path $outDir "createdump.exe") -Force -ErrorAction SilentlyContinue

# 生成版本清单（相对路径 + SHA256），供应用内增量更新与跨版本残留清理
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
Write-Host "发布完成: $outDir\FileManage.exe（便携文件夹，数据写入 Data\ 子目录）" -ForegroundColor Green

if (-not $SkipZip)
{
    $zipPath = Join-Path $root "FileManage-portable.zip"
    Compress-Archive -Path (Join-Path $outDir "*") -DestinationPath $zipPath -Force
    Write-Host "打包完成: $zipPath" -ForegroundColor Green
}
