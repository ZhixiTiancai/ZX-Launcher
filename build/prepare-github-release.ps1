param([string]$Version='',[string]$Repository='ZhixiTiancai/ZX-Launcher',[string]$Python='')
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
if($Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$'){throw 'Invalid GitHub repository'}
if($Version){& (Join-Path $PSScriptRoot 'set-version.ps1') -Version $Version}
$Version=[regex]::Match([IO.File]::ReadAllText((Join-Path $taskRoot 'src\Program.cs')),'AssemblyVersion\("(\d+\.\d+\.\d+)\.\d+"\)').Groups[1].Value
$notes=[IO.File]::ReadAllText((Join-Path $taskRoot 'release-notes.md'))
if($notes.Length -gt 6000 -or -not $notes.Contains($Version)){throw 'Update release-notes.md with this version and changes before preparing a release'}
& (Join-Path $PSScriptRoot 'build.ps1')
& (Join-Path $PSScriptRoot 'PackTools.exe') --self-test
if($LASTEXITCODE -ne 0){throw 'Self-tests failed'}
& (Join-Path $PSScriptRoot 'package-program-update.ps1') -BaseUrl ('https://github.com/'+$Repository+'/releases/download/v'+$Version) -AssetName ('ZX-Launcher-'+$Version+'-update.zip') -ReleaseNotes $notes
if(-not $Python){$found=Get-Command python -ErrorAction SilentlyContinue;if($found){$Python=$found.Source}else{throw 'Python 3.11+ is needed for the clean friend ZIP; pass -Python with its full path'}}
& $Python (Join-Path $PSScriptRoot 'package-friends.py')
if($LASTEXITCODE -ne 0){throw 'Friend ZIP packaging failed'}
$dist=Join-Path $taskRoot 'dist'
$folder=Join-Path $dist ('github-v'+$Version)
New-Item -ItemType Directory -Path $folder -Force | Out-Null
$items=[ordered]@{
 ('ZX-Launcher-'+$Version+'-update.zip')=('ZX-Launcher-'+$Version+'-update.zip')
 ('ZX-Launcher-'+$Version+'-update.exe')=('智熄的启动器-'+$Version+'-保留数据更新.exe')
 'ZX-Launcher-Windows-x64.zip'=('智熄的启动器-'+$Version+'-朋友分享版-不含整合包-Windows64.zip')
 'update.json'='update.json'
}
$sums=@();foreach($pair in $items.GetEnumerator()){$to=Join-Path $folder $pair.Key;Copy-Item -LiteralPath (Join-Path $dist $pair.Value) -Destination $to -Force;$sums+=((Get-FileHash -LiteralPath $to -Algorithm SHA256).Hash.ToLowerInvariant()+'  '+$pair.Key)}
[IO.File]::WriteAllLines((Join-Path $folder 'SHA256SUMS.txt'),$sums,(New-Object Text.UTF8Encoding($false)))
Write-Output ('Release files ready: '+$folder)
Write-Output 'Next: run build/publish-github.ps1 to review, then add -Publish to upload and publish.'
