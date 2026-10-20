# PowerShell | ExternalEsp/ImportOffsets.ps1 | Windows | Convert a supplied HPP table to inert C# constants, retain its source hash and declared build.
param([Parameter(Mandatory=$true)][string]$SourceFile)
$ErrorActionPreference='Stop'
$inputPath=[IO.Path]::GetFullPath($SourceFile)
$text=[IO.File]::ReadAllText($inputPath)
$version=[regex]::Match($text,'ClientVersion\s*=\s*"(version-[a-f0-9]{16})"').Groups[1].Value
if(-not $version){throw 'Missing or unsupported build identifier'}
$groups=[ordered]@{}
$currentGroup=$null
foreach($line in ($text -split '\r?\n')){
 $groupMatch=[regex]::Match($line,'^\s*namespace\s+([A-Za-z_]\w*)\s*\{')
 if($groupMatch.Success -and $groupMatch.Groups[1].Value -ne 'Offsets'){$currentGroup=$groupMatch.Groups[1].Value;$groups[$currentGroup]=@()}
 $field=[regex]::Match($line,'^\s*inline\s+constexpr\s+uintptr_t\s+([A-Za-z_]\w*)\s*=\s*(0x[a-fA-F0-9]+)\s*;')
 if($field.Success){if(-not $currentGroup){throw 'Field outside a group'};$value=[Convert]::ToInt64($field.Groups[2].Value.Substring(2),16);$groups[$currentGroup]+=@{name=$field.Groups[1].Value;hex=$field.Groups[2].Value;value=$value}}
}
$count=($groups.Values | ForEach-Object {$_.Count} | Measure-Object -Sum).Sum
if($count -lt 1 -or $count -gt 10000){throw 'Unexpected offset count'}
$lines=New-Object 'Collections.Generic.List[string]'
$lines.Add('// C# | ExternalEsp/Offsets.cs | .NET Framework 4.8 x64 | Generated constants from supplied HPP; not independently validated addresses.')
$lines.Add('namespace Offsets {')
$lines.Add((' public static class Info { public const string ClientVersion = "'+$version+'"; }'))
foreach($group in $groups.Keys){$lines.Add((' public static class '+$group+' {'));foreach($field in $groups[$group]){$lines.Add(('  public const long '+$field.name+' = '+$field.hex+';'))};$lines.Add(' }')}
$lines.Add('}')
[IO.File]::WriteAllLines((Join-Path $PSScriptRoot 'Offsets.cs'),$lines)
$profile=Join-Path $PSScriptRoot 'profile'
New-Item -ItemType Directory -Force -Path $profile | Out-Null
Copy-Item -LiteralPath $inputPath -Destination (Join-Path $profile 'supplied-offsets.hpp') -Force
@{version=$version;sourceSha256=(Get-FileHash -LiteralPath $inputPath).Hash;constants=$count;headerDeclaredConstants=[regex]::Match($text,'Total Offsets\s*:\s*(\d+)').Groups[1].Value;source='User-supplied HPP from https://offsets.imtheo.lol/';zeroValues=@($groups.Values | ForEach-Object {$_} | Where-Object value -eq 0).Count;validation='Version label and syntax only; runtime reads independently checked'} | ConvertTo-Json -Depth 3 | Set-Content -LiteralPath (Join-Path $profile 'provenance.json') -Encoding utf8
Write-Output "Imported $count constants for $version"
