$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'test.ps1')
$bin=Join-Path $PSScriptRoot 'src/AutoDrawing/bin/Release/net48'
$version=[Reflection.AssemblyName]::GetAssemblyName((Join-Path $bin 'AutoDrawing.dll')).Version.ToString(3)
$destination=Join-Path $PSScriptRoot "release/AutoDrawing-$version"
New-Item -ItemType Directory -Path $destination -Force | Out-Null
foreach($name in @('AutoDrawing.dll','AutoDrawing.Launcher.exe','AutoDrawing.Cli.exe','SolidWorks.Interop.sldworks.dll','SolidWorks.Interop.swconst.dll','SolidWorks.Interop.swpublished.dll')) {
    Copy-Item -LiteralPath (Join-Path $bin $name) -Destination $destination -Force
}
foreach($name in @('install.ps1','README.md','CHANGELOG.md','LICENSE')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination $destination -Force }
New-Item -ItemType Directory -Path (Join-Path $destination 'scripts') -Force | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'scripts/review_pdf.py') -Destination (Join-Path $destination 'scripts') -Force
foreach($folder in @('examples','docs')) {
    New-Item -ItemType Directory -Path (Join-Path $destination $folder) -Force | Out-Null
}
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'examples') -File | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $destination 'examples') -Force }
foreach($name in @('external-api.md','gearing-formulas.md')) { Copy-Item -LiteralPath (Join-Path $PSScriptRoot "docs/$name") -Destination (Join-Path $destination 'docs') -Force }
Compress-Archive -Path "$destination/*" -DestinationPath (Join-Path $PSScriptRoot "release/AutoDrawing-$version.zip") -Force
# Keep the original entry point current when it is not loaded by a running process.
# Never terminate a running UI or overwrite part of its loaded binary set.
$stable=Join-Path $PSScriptRoot 'release/AutoDrawing'
New-Item -ItemType Directory -Path $stable -Force | Out-Null
$locks=[System.Collections.Generic.List[System.IO.FileStream]]::new()
$canUpdate=$true
try {
    Get-ChildItem -LiteralPath $destination -File | Where-Object Extension -In '.dll','.exe' | ForEach-Object {
        $existing=Join-Path $stable $_.Name
        if(Test-Path -LiteralPath $existing) { $locks.Add([IO.File]::Open($existing,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)) }
    }
} catch { $canUpdate=$false; Write-Warning '原 release/AutoDrawing 入口正在使用或不可写，保留原目录；请使用根目录“启动自动出图.cmd”启动最新版本。' }
finally { foreach($stream in $locks) { $stream.Dispose() } }
if($canUpdate) { Get-ChildItem -LiteralPath $destination | ForEach-Object { Copy-Item -LiteralPath $_.FullName -Destination $stable -Recurse -Force }; Write-Host "Stable entry updated: $stable" }
$entry='@echo off'+[Environment]::NewLine+'start "" "%~dp0release\AutoDrawing-'+$version+'\AutoDrawing.Launcher.exe"'+[Environment]::NewLine
[IO.File]::WriteAllText((Join-Path $PSScriptRoot '启动自动出图.cmd'),$entry,[Text.Encoding]::ASCII)
Write-Host "Package: $destination"
