# FileManage 发布脚本：自包含单文件 win-x64
# 用法: powershell -ExecutionPolicy Bypass -File scripts/publish.ps1
param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$outDir = Join-Path $root "publish"

dotnet publish (Join-Path $root "src/FileManage.App/FileManage.App.csproj") `
    -c $Configuration -r $Runtime --self-contained true `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:EnableCompressionInSingleFile=true `
    -o $outDir

if ($LASTEXITCODE -ne 0)
{
    Write-Host "发布失败（dotnet publish 退出码 $LASTEXITCODE）" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host ""
Write-Host "发布完成: $outDir\FileManage.exe" -ForegroundColor Green
Write-Host "（自包含单文件，目标机器无需安装 .NET 运行时）"
