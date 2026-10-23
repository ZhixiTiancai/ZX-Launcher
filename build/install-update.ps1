param([Parameter(Mandatory=$true)][string]$TargetRoot,[switch]$ProgramOnly)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$target=[IO.Path]::GetFullPath($TargetRoot)
$version=[regex]::Match([IO.File]::ReadAllText((Join-Path $taskRoot 'src\Program.cs')),'AssemblyVersion\("(\d+\.\d+\.\d+)\.\d+"\)').Groups[1].Value
$package=Join-Path $taskRoot ('dist\ZX-Launcher-'+$version+'-update.zip')
if(-not(Test-Path -LiteralPath $package)){$package=Join-Path $taskRoot ('dist\智熄的启动器-'+$version+'-程序更新.zip')}
if(-not(Test-Path -LiteralPath $package)){throw '先运行 package-program-update.ps1 生成并验证签名更新包。'}
& (Join-Path $PSScriptRoot 'PackTools.exe') --apply-update $target $package
if($LASTEXITCODE -ne 0){throw '更新未完成，原程序和用户数据已保留。'}
$entry=Join-Path $target '智熄的启动器.exe'
$migration=Start-Process -FilePath $entry -ArgumentList '--migrate-only' -WorkingDirectory $target -WindowStyle Hidden -PassThru -Wait
if($migration.ExitCode -ne 0){throw '程序已更新，但目录整理未完成；请查看 minecraft/launcher/startup-error.txt。'}
Write-Output ('Updated and organized: '+$target)
