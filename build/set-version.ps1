param([Parameter(Mandatory=$true)][ValidatePattern('^\d{1,4}\.\d{1,4}\.\d$')][string]$Version)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$utf8=New-Object Text.UTF8Encoding($true)
foreach($relative in @('src\Program.cs','build\UpdaterProgram.cs')){
 $path=Join-Path $taskRoot $relative
 $text=[IO.File]::ReadAllText($path)
 $text=[regex]::Replace($text,'AssemblyVersion\("\d+\.\d+\.\d+\.\d+"\)','AssemblyVersion("'+$Version+'.0")')
 [IO.File]::WriteAllText($path,$text,$utf8)
}
$path=Join-Path $taskRoot 'src\App.xaml'
$text=[regex]::Replace([IO.File]::ReadAllText($path),'Text="\d+\.\d+\.\d+"','Text="'+$Version+'"')
[IO.File]::WriteAllText($path,$text,$utf8)
foreach($relative in @('README.md','朋友版说明.txt','使用说明.txt')){
 $path=Join-Path $taskRoot $relative
 $text=[regex]::Replace([IO.File]::ReadAllText($path),'\A(# )?智熄的启动器 \d+\.\d+\.\d+',{'智熄的启动器 '+$Version})
 if($relative -eq 'README.md'){$text='# '+$text.TrimStart('#',' ')}
 [IO.File]::WriteAllText($path,$text,$utf8)
}
Write-Output ('Version synchronized: '+$Version)
