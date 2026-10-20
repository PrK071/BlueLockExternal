# PowerShell | ExternalEsp/build.ps1 | Windows x64 / .NET Framework 4.8 | Standalone ESP build, no mutation of V2 binaries or historical reports.
param([string]$OutputDirectory=(Join-Path $PSScriptRoot 'bin'))
$ErrorActionPreference='Stop'
if(-not (Test-Path -LiteralPath (Join-Path $PSScriptRoot 'Offsets.cs'))){throw 'Run ImportOffsets.ps1 with the supplied HPP first'}
$bin=[IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Force -Path $bin | Out-Null
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
$sources=@(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs' -File | Select-Object -ExpandProperty FullName)
& $compiler /nologo /platform:x64 /optimize+ /warn:4 /target:exe /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll /r:System.Web.Extensions.dll ('/out:'+(Join-Path $bin 'BlueLockEsp.exe')) @sources
if($LASTEXITCODE -ne 0){throw 'ESP build failed'}
[IO.File]::WriteAllText((Join-Path $bin 'BlueLockEsp.exe.config'),'<?xml version="1.0"?><configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8"/></startup></configuration>')
Write-Output (Join-Path $bin 'BlueLockEsp.exe')
