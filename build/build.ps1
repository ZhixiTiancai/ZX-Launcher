$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $taskRoot 'release\FoolLauncher'
New-Item -ItemType Directory -Force -Path $output | Out-Null
& (Join-Path $PSScriptRoot 'fetch-webview.ps1')
& (Join-Path $PSScriptRoot 'build-updater.ps1')
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$refs = @('Microsoft.VisualBasic.dll','System.dll','System.Core.dll','System.Security.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$refs += @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.Wpf.dll') | ForEach-Object { '/reference:' + (Join-Path $output $_) }
$sources = Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
$iconResource = '/resource:' + (Join-Path $taskRoot 'src\assets\default-grass.png') + ',FoolLauncher.DefaultGrass.png'
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:x64 /optimize+ /utf8output /codepage:65001 ('/out:' + (Join-Path $output '智熄的启动器.exe')) $iconResource @refs @sources
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /optimize+ /utf8output /codepage:65001 ('/out:' + (Join-Path $taskRoot 'build\PackTools.exe')) $iconResource @refs @sources
if ($LASTEXITCODE -ne 0) { throw '工具编译失败' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'src\App.xaml') -Destination (Join-Path $output 'App.xaml') -Force
Write-Output $output
