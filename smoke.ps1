param(
    [Parameter(Mandatory=$true)][string]$Model,
    [string]$Template='C:\ProgramData\SOLIDWORKS\SOLIDWORKS 2026\templates\gb_a3.drwdot',
    [string]$OutputDirectory=(Join-Path $PSScriptRoot 'artifacts/smoke')
)
$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'build.ps1')
$bin=Join-Path $PSScriptRoot 'src/AutoDrawing/bin/Release/net48'
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $csc /nologo /platform:x64 "/out:$bin/Smoke.exe" "/r:$bin/AutoDrawing.dll" "/r:$bin/SolidWorks.Interop.sldworks.dll" (Join-Path $PSScriptRoot 'tests/Smoke.cs')
if($LASTEXITCODE -ne 0) { throw 'Smoke test compilation failed' }
& "$bin/Smoke.exe" ([IO.Path]::GetFullPath($Model)) ([IO.Path]::GetFullPath($Template)) ([IO.Path]::GetFullPath($OutputDirectory))
if($LASTEXITCODE -ne 0) { throw 'Native smoke test failed; see report.json' }
