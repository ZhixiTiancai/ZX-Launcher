param([string]$EmbeddedPackage,[string]$OutputFile)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
if(-not $OutputFile){$OutputFile=Join-Path $taskRoot 'release\FoolLauncher\智熄更新器.exe'}
$framework='C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$refs=@('System.dll','System.Core.dll','System.Windows.Forms.dll','System.Security.dll','System.Web.Extensions.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')|ForEach-Object{'/reference:'+(Join-Path $framework $_)}
$extra=@();if($EmbeddedPackage){$extra+=('/resource:'+[IO.Path]::GetFullPath($EmbeddedPackage)+',FoolLauncher.Update.zip')}
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:x64 /optimize+ /utf8output /codepage:65001 ('/out:'+$OutputFile) @refs @extra (Join-Path $PSScriptRoot 'UpdaterProgram.cs') (Join-Path $taskRoot 'src\LauncherUpdateCore.cs') (Join-Path $taskRoot 'src\UpdateTrust.cs')
if($LASTEXITCODE -ne 0){throw '更新器编译失败'}
Write-Output $OutputFile
