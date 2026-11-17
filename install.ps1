param([switch]$Uninstall)
$ErrorActionPreference='Stop'
$principal=[Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()
if(-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw '请在管理员 PowerShell 中运行此脚本：注册 .NET Framework COM 插件需要写入 HKLM。'
}
$guid='{B738EE37-3079-4CBA-9DC3-9A70AB9CB164}'
$dll=Join-Path $PSScriptRoot 'src/AutoDrawing/bin/Release/net48/AutoDrawing.dll'
if(Test-Path -LiteralPath (Join-Path $PSScriptRoot 'AutoDrawing.dll')) { $dll=Join-Path $PSScriptRoot 'AutoDrawing.dll' }
$regasm=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/RegAsm.exe'
if(-not (Test-Path -LiteralPath $dll)) { throw '请先运行 .\build.ps1。' }
$addinKey="HKLM:\SOFTWARE\SolidWorks\AddIns\$guid"
$startupKey="HKCU:\SOFTWARE\SolidWorks\AddInsStartup\$guid"
if($Uninstall) {
    & $regasm $dll /unregister /nologo
    if($LASTEXITCODE -ne 0) { throw 'COM 取消注册失败' }
    foreach($key in @($addinKey,$startupKey)) { if(Test-Path -LiteralPath $key) { Remove-Item -LiteralPath $key -Force } }
    Write-Host '已卸载 AutoDrawing 注册项；源码和输出文件保留。'
} else {
    & $regasm $dll /codebase /nologo
    if($LASTEXITCODE -ne 0) { throw 'COM 注册失败' }
    New-Item -Path $addinKey -Force | Out-Null
    Set-Item -LiteralPath $addinKey -Value 0
    New-ItemProperty -LiteralPath $addinKey -Name Title -Value 'AutoDrawing 自动出图' -PropertyType String -Force | Out-Null
    New-ItemProperty -LiteralPath $addinKey -Name Description -Value '3D 转 2D、自动标注、同源尺寸去重和批量导出' -PropertyType String -Force | Out-Null
    New-Item -Path $startupKey -Force | Out-Null
    Set-Item -LiteralPath $startupKey -Value 0
    Write-Host '已注册。请在 SolidWorks → 工具 → 插件 中勾选 AutoDrawing 自动出图，再打开工具菜单的 自动出图 → 批量出图。'
}
