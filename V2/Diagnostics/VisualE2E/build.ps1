# PowerShell | Diagnostics/VisualE2E/build.ps1 | Windows x64 | Separate artifacts; never replace the open application's DLLs.
param([string]$OutputDirectory=(Join-Path $PSScriptRoot '../../reports/visual-e2e-20261018/bin'))
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$bin=[IO.Path]::GetFullPath($OutputDirectory)
& (Join-Path $root 'build.ps1') -OutputDirectory $bin
$refs=@('/r:System.dll','/r:System.Core.dll','/r:System.Drawing.dll','/r:System.Windows.Forms.dll','/r:System.Web.Extensions.dll',('/r:'+(Join-Path $bin 'BlueLockV2.Core.dll')),('/r:'+(Join-Path $bin 'OpenCvSharp.dll')),('/r:'+(Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\netstandard.dll')))
foreach($name in @('Runner','Probe','NativePulse','Relay')){
 if($name -eq 'Relay' -and (Test-Path -LiteralPath (Join-Path $bin 'Relay.exe')) -and (Get-Item -LiteralPath (Join-Path $bin 'Relay.exe')).LastWriteTimeUtc -ge (Get-Item -LiteralPath (Join-Path $PSScriptRoot 'Relay.cs')).LastWriteTimeUtc){continue}
 & (Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe') /nologo /platform:x64 @refs ('/out:'+(Join-Path $bin ($name+'.exe'))) (Join-Path $PSScriptRoot ($name+'.cs'))
 if($LASTEXITCODE -ne 0){throw "Compilation failed: $name"}
 Copy-Item -LiteralPath (Join-Path $bin 'BlueLockExternalV2.exe.config') -Destination (Join-Path $bin ($name+'.exe.config')) -Force
}
