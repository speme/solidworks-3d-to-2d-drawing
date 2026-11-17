$ErrorActionPreference='Stop'
& (Join-Path $PSScriptRoot 'build.ps1')
$bin=Join-Path $PSScriptRoot 'src/AutoDrawing/bin/Release/net48'
$csc=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $csc /nologo /platform:x64 "/out:$bin/Checks.exe" "/r:$bin/AutoDrawing.dll" (Join-Path $PSScriptRoot 'tests/Checks.cs')
if($LASTEXITCODE -ne 0) { throw 'Test compilation failed' }
& "$bin/Checks.exe"
if($LASTEXITCODE -ne 0) { throw 'Tests failed' }
& $csc /nologo /platform:x64 "/out:$bin/ApiChecks.exe" "/r:$bin/AutoDrawing.dll" /r:System.Web.Extensions.dll (Join-Path $PSScriptRoot 'tests/ApiChecks.cs')
if($LASTEXITCODE -ne 0) { throw 'API test compilation failed' }
& "$bin/ApiChecks.exe"
if($LASTEXITCODE -ne 0) { throw 'API tests failed' }
& $csc /nologo /platform:x64 "/out:$bin/GearingChecks.exe" "/r:$bin/AutoDrawing.dll" (Join-Path $PSScriptRoot 'tests/GearingChecks.cs')
if($LASTEXITCODE -ne 0) { throw 'Gearing checks compilation failed' }
& "$bin/GearingChecks.exe"
if($LASTEXITCODE -ne 0) { throw 'Gearing formula tests failed' }
