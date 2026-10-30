$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $taskRoot 'release\FoolLauncher'
New-Item -ItemType Directory -Force -Path $output | Out-Null
& (Join-Path $PSScriptRoot 'fetch-webview.ps1')
& (Join-Path $PSScriptRoot 'build-updater.ps1')
$framework = 'C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
$refs = @('System.Windows.Forms.dll','Microsoft.VisualBasic.dll','System.dll','System.Core.dll','System.Security.dll','System.Net.Http.dll','System.Web.Extensions.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll') | ForEach-Object { '/reference:' + (Join-Path $framework $_) }
$refs += @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.Wpf.dll') | ForEach-Object { '/reference:' + (Join-Path $output $_) }
$refs += @('UIAutomationClient.dll','UIAutomationTypes.dll') | ForEach-Object { '/reference:' + (Join-Path $framework ('WPF\'+$_)) }
$sources = Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Filter '*.cs' | Select-Object -ExpandProperty FullName
$iconResource = '/resource:' + (Join-Path $taskRoot 'src\assets\default-grass.png') + ',FoolLauncher.DefaultGrass.png'
$logoResource='/resource:'+(Join-Path $taskRoot 'src\assets\launcher-logo.png')+',FoolLauncher.Logo.png'
$winIcon='/win32icon:'+(Join-Path $taskRoot 'build\launcher.ico')
$shortcutResource='/resource:'+(Join-Path $taskRoot 'build\launcher.ico')+',Zhixi.LauncherIcon.ico'
$inner=Join-Path $taskRoot 'build\LauncherApp.exe'
& (Join-Path $framework 'csc.exe') /nologo /main:FoolLauncher.Program /target:winexe /platform:x64 /optimize+ /utf8output /codepage:65001 ('/out:' + $inner) $winIcon $shortcutResource $logoResource $iconResource @refs @sources
if ($LASTEXITCODE -ne 0) { throw '编译失败' }
& (Join-Path $framework 'csc.exe') /nologo /main:FoolLauncher.Program /target:exe /platform:x64 /optimize+ /utf8output /codepage:65001 ('/out:' + (Join-Path $taskRoot 'build\PackTools.exe')) $shortcutResource $logoResource $iconResource @refs @sources
if ($LASTEXITCODE -ne 0) { throw '工具编译失败' }
$versionFile=Join-Path $taskRoot 'build\PortableVersion.cs'
$versionText=[regex]::Match([IO.File]::ReadAllText((Join-Path $taskRoot 'src\Program.cs')),'\[assembly:System.Reflection.AssemblyVersion\("[^"]+"\)\]').Value
[IO.File]::WriteAllText($versionFile,('[assembly:System.Reflection.AssemblyTitle("智熄的启动器")]'+[Environment]::NewLine+$versionText),[Text.Encoding]::UTF8)
& (Join-Path $framework 'csc.exe') /nologo /main:FoolLauncher.PortableEntry /target:winexe /platform:x64 /optimize+ /utf8output /codepage:65001 ('/out:'+(Join-Path $output '智熄的启动器.exe')) $winIcon $shortcutResource ('/resource:'+$inner+',Zhixi.LauncherApp.exe') @refs $versionFile (Join-Path $taskRoot 'src\PortableEntry.cs') (Join-Path $taskRoot 'src\PortableLayout.cs') (Join-Path $taskRoot 'src\ShortcutIcons.cs')
if ($LASTEXITCODE -ne 0) { throw '便携入口编译失败' }
Copy-Item -LiteralPath (Join-Path $taskRoot 'src\App.xaml') -Destination (Join-Path $output 'App.xaml') -Force
Write-Output $output
