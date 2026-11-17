param([string]$FixtureDirectory=(Join-Path $PSScriptRoot 'artifacts/v1.1/fixtures'),[string]$Template='C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2026\templates\gb_a3.drwdot')
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'build.ps1')
$bin=Join-Path $PSScriptRoot 'src/AutoDrawing/bin/Release/net48'
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $csc /nologo /platform:x64 "/out:$bin/Regression.exe" "/r:$bin/AutoDrawing.dll" "/r:$bin/SolidWorks.Interop.sldworks.dll" (Join-Path $PSScriptRoot 'tests/Regression.cs')
if($LASTEXITCODE -ne 0) { throw 'Regression compilation failed' }
& "$bin/Regression.exe" $FixtureDirectory $Template
if($LASTEXITCODE -ne 0) { throw 'Native regression failed; see batch report' }
