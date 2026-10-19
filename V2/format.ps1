# PowerShell | format.ps1 | PowerShell 7 with Roslyn | Optional source formatting; no application dependency.
$ErrorActionPreference='Stop'
Add-Type -TypeDefinition @'
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
public static class BlueLockSourceFormatter {
 public static string Format(string source) {
  var tree=CSharpSyntaxTree.ParseText(source);
  foreach(var error in tree.GetDiagnostics())
   if(error.Severity==DiagnosticSeverity.Error) throw new System.Exception(error.ToString());
  return tree.GetRoot().NormalizeWhitespace("    ","\r\n",false).ToFullString()+"\r\n";
 }
}
'@ -CompilerOptions '/nowarn:1701' -ReferencedAssemblies @((Join-Path $PSHOME 'Microsoft.CodeAnalysis.dll'),(Join-Path $PSHOME 'Microsoft.CodeAnalysis.CSharp.dll'))
foreach($taskDirectory in @('Core','Capture','Vision','Tracking','Prediction','Decision','Input','Configuration','Diagnostics','UI','Tests')) {
 foreach($taskSource in Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot $taskDirectory) -Filter '*.cs' -File) {
  $taskFormatted=[BlueLockSourceFormatter]::Format([IO.File]::ReadAllText($taskSource.FullName))
  [IO.File]::WriteAllText($taskSource.FullName,$taskFormatted,[Text.UTF8Encoding]::new($true))
 }
}
