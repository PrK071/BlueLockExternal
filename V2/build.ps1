# PowerShell | build.ps1 | Windows x64 / .NET Framework 4.8 | Reproducible pinned NuGet restore and C# compilation.
param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$taskRoot = $PSScriptRoot
$taskPackages = Join-Path $taskRoot 'packages'
New-Item -ItemType Directory -Force -Path $taskPackages | Out-Null
New-Item -ItemType Directory -Force -Path (Join-Path $taskRoot 'reports') | Out-Null
$taskLock=Get-Content -Raw -LiteralPath (Join-Path $taskRoot 'dependencies.lock.json') | ConvertFrom-Json
$taskVersions = [ordered]@{'opencvsharp4'='4.13.0.20260627';'opencvsharp4.runtime.win'='4.13.0.20260627';'system.memory'='4.6.3';'system.buffers'='4.6.1';'system.numerics.vectors'='4.6.1';'system.runtime.compilerservices.unsafe'='6.1.2'}

Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach ($taskPackage in $taskVersions.Keys) {
 $taskVersion=$taskVersions[$taskPackage];$taskZip=Join-Path $taskPackages ($taskPackage+'.nupkg');$taskFolder=Join-Path $taskPackages $taskPackage
 if (!(Test-Path -LiteralPath $taskZip)) { Invoke-WebRequest -Uri "https://api.nuget.org/v3-flatcontainer/$taskPackage/$taskVersion/$taskPackage.$taskVersion.nupkg" -OutFile $taskZip }
 $taskExpected=$taskLock.$taskPackage
 if($taskExpected.version -ne $taskVersion -or (Get-FileHash -LiteralPath $taskZip -Algorithm SHA256).Hash -ne $taskExpected.sha256){throw "Package integrity mismatch: $taskPackage"}
 if (!(Test-Path -LiteralPath $taskFolder)) { [IO.Compression.ZipFile]::ExtractToDirectory($taskZip,$taskFolder) }
}
$taskBin=if($OutputDirectory){[IO.Path]::GetFullPath($OutputDirectory)}else{Join-Path $taskRoot 'bin'}
New-Item -ItemType Directory -Force -Path $taskBin | Out-Null
$taskReferences=@('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Xml.dll','System.Web.Extensions.dll',(Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\netstandard.dll'))
$taskManaged=@((Join-Path $taskPackages 'opencvsharp4\lib\netstandard2.0\OpenCvSharp.dll'))
foreach($taskPackage in @('system.memory','system.buffers','system.numerics.vectors','system.runtime.compilerservices.unsafe')) { $taskManaged+=@(Get-ChildItem -LiteralPath (Join-Path $taskPackages "$taskPackage\lib\net462") -Filter '*.dll' | Select-Object -ExpandProperty FullName) }
$taskReferences+=$taskManaged
foreach($taskDll in $taskManaged){Copy-Item -LiteralPath $taskDll -Destination $taskBin -Force}
$taskNative=Join-Path $taskPackages 'opencvsharp4.runtime.win\runtimes\win-x64\native'
Copy-Item -Path (Join-Path $taskNative '*.dll') -Destination $taskBin -Force
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskCommon=@('/nologo','/platform:x64','/optimize+','/debug:pdbonly','/warn:4')+@($taskReferences | ForEach-Object { '/reference:'+$_ })
$taskSources=@()
foreach($taskDirectory in @('Core','Capture','Vision','Tracking','Prediction','Decision','Input','Configuration','Diagnostics','Navigation','BallControl')) { $taskSources+=@(Get-ChildItem -LiteralPath (Join-Path $taskRoot $taskDirectory) -Filter '*.cs' -File | Select-Object -ExpandProperty FullName) }
& $taskCompiler @taskCommon '/target:library' ('/out:'+(Join-Path $taskBin 'BlueLockV2.Core.dll')) @taskSources
if($LASTEXITCODE -ne 0){throw 'Core compilation failed'}
$taskUi=@(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'UI') -Filter '*.cs' -File | Select-Object -ExpandProperty FullName)
if($taskUi.Count){& $taskCompiler @taskCommon '/target:winexe' ('/reference:'+(Join-Path $taskBin 'BlueLockV2.Core.dll')) ('/out:'+(Join-Path $taskBin 'BlueLockExternalV2.exe')) @taskUi;if($LASTEXITCODE -ne 0){throw 'UI compilation failed'}}
$taskTests=@(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'Tests') -Filter '*.cs' -File | Select-Object -ExpandProperty FullName)
if($taskTests.Count){& $taskCompiler @taskCommon '/target:exe' ('/reference:'+(Join-Path $taskBin 'BlueLockV2.Core.dll')) ('/out:'+(Join-Path $taskBin 'BlueLockV2.Tests.exe')) @taskTests;if($LASTEXITCODE -ne 0){throw 'Tests compilation failed'}}
$taskRedirects=''
& $taskCompiler @taskCommon '/target:exe' ('/reference:'+(Join-Path $taskBin 'BlueLockV2.Core.dll')) ('/out:'+(Join-Path $taskBin 'BlueLockV2.NavigationTests.exe')) @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'Tests\Navigation') -Filter '*.cs' -File | Select-Object -ExpandProperty FullName)
if($LASTEXITCODE -ne 0){throw 'Navigation acceptance compilation failed'}
& $taskCompiler @taskCommon '/target:exe' ('/reference:'+(Join-Path $taskBin 'BlueLockV2.Core.dll')) ('/out:'+(Join-Path $taskBin 'BlueLockV2.BallTests.exe')) @(Get-ChildItem -LiteralPath (Join-Path $taskRoot 'Tests\BallControl') -Filter '*.cs' -File | Select-Object -ExpandProperty FullName)
if($LASTEXITCODE -ne 0){throw 'Ball control acceptance compilation failed'}
foreach($taskDll in $taskManaged){$taskAssembly=[Reflection.AssemblyName]::GetAssemblyName($taskDll);$taskToken=([BitConverter]::ToString($taskAssembly.GetPublicKeyToken())).Replace('-','').ToLowerInvariant();if($taskToken){$taskRedirects+='<dependentAssembly><assemblyIdentity name="'+$taskAssembly.Name+'" publicKeyToken="'+$taskToken+'" culture="neutral"/><bindingRedirect oldVersion="0.0.0.0-'+$taskAssembly.Version+'" newVersion="'+$taskAssembly.Version+'"/></dependentAssembly>'}}
$taskConfig='<?xml version="1.0"?><configuration><startup><supportedRuntime version="v4.0" sku=".NETFramework,Version=v4.8"/></startup><runtime><assemblyBinding xmlns="urn:schemas-microsoft-com:asm.v1">'+$taskRedirects+'</assemblyBinding></runtime></configuration>'
foreach($taskExe in @('BlueLockExternalV2.exe','BlueLockV2.Tests.exe','BlueLockV2.NavigationTests.exe','BlueLockV2.BallTests.exe')){[IO.File]::WriteAllText((Join-Path $taskBin ($taskExe+'.config')),$taskConfig)}
$taskVersions | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $taskBin 'packages.lock.json')
if(Test-Path -LiteralPath (Join-Path $taskRoot 'licenses')){Copy-Item -LiteralPath (Join-Path $taskRoot 'licenses') -Destination $taskBin -Recurse -Force}
if(Test-Path -LiteralPath (Join-Path $taskRoot 'NOTICE.md')){Copy-Item -LiteralPath (Join-Path $taskRoot 'NOTICE.md') -Destination $taskBin -Force}
Get-FileHash -Path (Join-Path $taskPackages '*.nupkg') -Algorithm SHA256 | Select-Object @{n='Package';e={Split-Path $_.Path -Leaf}},Hash | Export-Csv -LiteralPath (Join-Path $taskRoot 'reports\package-hashes.csv') -NoTypeInformation
Write-Output "Build completed: $taskBin"
Copy-Item -LiteralPath (Join-Path $taskRoot 'StudioLab') -Destination $taskBin -Recurse -Force
