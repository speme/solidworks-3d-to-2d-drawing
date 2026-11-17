param([string]$SolidWorksDir='C:\Program Files\SOLIDWORKS Corp\SOLIDWORKS')
$ErrorActionPreference='Stop'
$project=Join-Path $PSScriptRoot 'src/AutoDrawing/AutoDrawing.csproj'
& dotnet build $project -c Release "-p:SolidWorksDir=$SolidWorksDir" --nologo
if($LASTEXITCODE -ne 0) { throw 'Build failed' }
$bin=Join-Path $PSScriptRoot 'src/AutoDrawing/bin/Release/net48'
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $csc /nologo /platform:x64 /target:winexe "/out:$bin/AutoDrawing.Launcher.exe" "/r:$bin/AutoDrawing.dll" "/r:$bin/SolidWorks.Interop.sldworks.dll" /r:System.Windows.Forms.dll (Join-Path $PSScriptRoot 'src/AutoDrawing/Launcher.cs')
if($LASTEXITCODE -ne 0) { throw 'Launcher build failed' }
& $csc /nologo /platform:x64 "/out:$bin/AutoDrawing.Cli.exe" "/r:$bin/AutoDrawing.dll" "/r:$bin/SolidWorks.Interop.sldworks.dll" (Join-Path $PSScriptRoot 'src/AutoDrawing/Cli.cs')
if($LASTEXITCODE -ne 0) { throw 'CLI build failed' }
