$ErrorActionPreference = 'Stop'
$surfCompiler = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $surfCompiler /nologo /target:winexe /platform:x64 /codepage:65001 "/out:$PSScriptRoot\SURF-Launcher.exe" /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Management.dll "$PSScriptRoot\Launcher.cs"
if ($LASTEXITCODE -ne 0) { throw 'Launcher compilation failed' }
