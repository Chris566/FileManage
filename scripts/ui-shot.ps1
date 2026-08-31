﻿# 深色模式 UI 验收截图：主窗口 + 规则管理窗口（含 DatePicker）
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName UIAutomationClient
Add-Type -AssemblyName UIAutomationTypes
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

Add-Type -Namespace Win32 -Name Native -MemberDefinition @"
[System.Runtime.InteropServices.DllImport("user32.dll", CharSet=System.Runtime.InteropServices.CharSet.Unicode)]
public static extern System.IntPtr FindWindow(string cls, string title);
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool PostMessage(System.IntPtr hWnd, uint msg, System.IntPtr w, System.IntPtr l);
"@

$outDir = Join-Path $PSScriptRoot "..\.tmp"
New-Item -ItemType Directory -Force -Path $outDir | Out-Null

function Save-Screen($path)
{
    Start-Sleep -Milliseconds 1200
    $bounds = [System.Windows.Forms.Screen]::PrimaryScreen.Bounds
    $bmp = New-Object System.Drawing.Bitmap $bounds.Width, $bounds.Height
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.CopyFromScreen($bounds.Location, [System.Drawing.Point]::Empty, $bounds.Size)
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $g.Dispose(); $bmp.Dispose()
    Write-Output "截图: $path"
}

# 1) 主窗口（复选框/滚动条深色效果）
Save-Screen (Join-Path $outDir "shot-main.png")

# 2) 打开规则管理（DatePicker/Calendar 所在窗口）
$proc = Get-Process -Name FileManage | Select-Object -First 1
$root = [System.Windows.Automation.AutomationElement]::FromHandle($proc.MainWindowHandle)
$cond = New-Object System.Windows.Automation.PropertyCondition(
    [System.Windows.Automation.AutomationElement]::ControlTypeProperty,
    [System.Windows.Automation.ControlType]::Button)
$buttons = $root.FindAll([System.Windows.Automation.TreeScope]::Descendants, $cond)
$btn = $null
foreach ($b in $buttons) { if ($b.Current.Name -like "规则管理*") { $btn = $b; break } }
if ($btn) { $btn.GetCurrentPattern([System.Windows.Automation.InvokePattern]::Pattern).Invoke() }
Start-Sleep -Milliseconds 1500
Save-Screen (Join-Path $outDir "shot-rules.png")

# 3) 关闭规则管理窗口
$hwnd = [Win32.Native]::FindWindow($null, "规则管理")
if ($hwnd -ne [System.IntPtr]::Zero)
{
    [Win32.Native]::PostMessage($hwnd, 0x0010, [System.IntPtr]::Zero, [System.IntPtr]::Zero) | Out-Null
    Write-Output "规则管理窗口已关闭"
}
else
{
    Write-Output "未找到规则管理窗口句柄"
}
Write-Output "完成"
