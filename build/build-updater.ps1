param([string]$EmbeddedPackage,[string]$OutputFile)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
if(-not $OutputFile){$OutputFile=Join-Path $taskRoot 'release\FoolLauncher\智熄更新器.exe'}
$framework='C:\Windows\Microsoft.NET\Framework64\v4.0.30319'
# ICO contains the supplied logo at the standard Explorer/taskbar sizes.
Add-Type -AssemblyName System.Drawing
$icon=Join-Path $taskRoot 'build\launcher.ico'
$original=[Drawing.Image]::FromFile((Join-Path $taskRoot 'src\assets\launcher-logo.png'))
try {
 $frames=@();foreach($size in @(16,24,32,48,64,128,256)){
  $bitmap=New-Object Drawing.Bitmap($size,$size);$graphics=[Drawing.Graphics]::FromImage($bitmap)
  try{$graphics.InterpolationMode=[Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic;$graphics.DrawImage($original,0,0,$size,$size);$memory=New-Object IO.MemoryStream;$bitmap.Save($memory,[Drawing.Imaging.ImageFormat]::Png);$frames+=,@{Size=$size;Bytes=$memory.ToArray()};$memory.Dispose()}finally{$graphics.Dispose();$bitmap.Dispose()}
 }
 $writer=New-Object IO.BinaryWriter([IO.File]::Create($icon))
 try{$writer.Write([uint16]0);$writer.Write([uint16]1);$writer.Write([uint16]$frames.Count);$offset=6+16*$frames.Count;foreach($frame in $frames){$dimension=if($frame.Size -eq 256){0}else{$frame.Size};$writer.Write([byte]$dimension);$writer.Write([byte]$dimension);$writer.Write([byte]0);$writer.Write([byte]0);$writer.Write([uint16]1);$writer.Write([uint16]32);$writer.Write([uint32]$frame.Bytes.Length);$writer.Write([uint32]$offset);$offset+=$frame.Bytes.Length};foreach($frame in $frames){$writer.Write([byte[]]$frame.Bytes)}}finally{$writer.Dispose()}
}finally{$original.Dispose()}
$refs=@('System.dll','System.Core.dll','System.Windows.Forms.dll','System.Security.dll','System.Web.Extensions.dll','System.IO.Compression.dll','System.IO.Compression.FileSystem.dll','System.Xaml.dll','WPF\WindowsBase.dll','WPF\PresentationCore.dll','WPF\PresentationFramework.dll')|ForEach-Object{'/reference:'+(Join-Path $framework $_)}
$extra=@();if($EmbeddedPackage){$extra+=('/resource:'+[IO.Path]::GetFullPath($EmbeddedPackage)+',FoolLauncher.Update.zip')}
& (Join-Path $framework 'csc.exe') /nologo /target:winexe /platform:x64 /optimize+ /utf8output /codepage:65001 ('/win32icon:'+$icon) ('/out:'+$OutputFile) @refs @extra (Join-Path $PSScriptRoot 'UpdaterProgram.cs') (Join-Path $taskRoot 'src\LauncherUpdateCore.cs') (Join-Path $taskRoot 'src\PortableLayout.cs') (Join-Path $taskRoot 'src\UpdateTrust.cs')
if($LASTEXITCODE -ne 0){throw '更新器编译失败'}
Write-Output $OutputFile
