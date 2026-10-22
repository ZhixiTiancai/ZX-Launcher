param([string]$BaseUrl='',[string]$AssetName='',[string]$ReleaseNotes='')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem
$taskRoot=Split-Path -Parent $PSScriptRoot
$release=Join-Path $taskRoot 'release\FoolLauncher'
$dist=Join-Path $taskRoot 'dist'
New-Item -ItemType Directory -Path $dist -Force | Out-Null
$version=[regex]::Match([IO.File]::ReadAllText((Join-Path $taskRoot 'src\Program.cs')),'AssemblyVersion\("(\d+\.\d+\.\d+)\.\d+"\)').Groups[1].Value
if(-not $version){throw 'Missing version'}
$key=Join-Path $taskRoot '.private\update-signing-key.xml'
if(-not(Test-Path -LiteralPath $key)){throw 'Publisher signing key missing. Restore the existing key; do not generate a replacement for an existing release line.'}
$rsa=New-Object Security.Cryptography.RSACryptoServiceProvider
$rsa.PersistKeyInCsp=$false
$rsa.FromXmlString([IO.File]::ReadAllText($key))
$utf8=New-Object Text.UTF8Encoding($false)
function SignedBytes($payload){
 $raw=$utf8.GetBytes(($payload|ConvertTo-Json -Depth 12 -Compress))
 $signature=$rsa.SignData($raw,[Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256'))
 $envelope=@{Payload=[Convert]::ToBase64String($raw);Signature=[Convert]::ToBase64String($signature)}|ConvertTo-Json -Compress
 return ,$utf8.GetBytes($envelope)
}
$items=[ordered]@{}
foreach($name in @('智熄的启动器.exe','智熄更新器.exe','App.xaml','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.Wpf.dll','WebView2Loader.dll','WebView2-LICENSE.txt')){$items[$name]=Join-Path $release $name}
$items['使用说明.txt']=Join-Path $taskRoot '朋友版说明.txt'
$items['THIRD-PARTY.txt']=Join-Path $taskRoot 'THIRD-PARTY.txt'
foreach($name in @('LICENSE','README.md','使用说明.txt','朋友版说明.txt','THIRD-PARTY.txt')){$items['source/'+$name]=Join-Path $taskRoot $name}
Get-ChildItem -LiteralPath (Join-Path $taskRoot 'src') -Recurse -File|ForEach-Object{$items['source/src/'+$_.FullName.Substring((Join-Path $taskRoot 'src').Length+1).Replace('\','/')]=$_.FullName}
foreach($name in @('build.ps1','build-updater.ps1','fetch-webview.ps1','fetch-deps.ps1','assemble.ps1','package-friends.py','package-program-update.ps1','install-update.ps1','initialize-update-key.ps1','UpdaterProgram.cs')){$items['source/build/'+$name]=Join-Path $PSScriptRoot $name}
$files=@();foreach($entry in $items.GetEnumerator()){$file=Get-Item -LiteralPath $entry.Value;$files+=@{Name=$entry.Key;Size=$file.Length;Sha256=(Get-FileHash -LiteralPath $entry.Value -Algorithm SHA256).Hash.ToLowerInvariant()}}
$notes=$ReleaseNotes
if(-not $notes){$notes=[IO.File]::ReadAllText((Join-Path $taskRoot 'release-notes.md'))}
if($notes.Length -gt 6000){throw 'Release notes must be at most 6000 characters'}
$manifest=@{Product='ZhixiLauncher';Format=1;Kind='package';Version=$version;Notes=$notes;Files=$files}
$signed=SignedBytes $manifest
$name='智熄的启动器-'+$version+'-程序更新.zip'
if($AssetName){if($AssetName -notmatch '\AZX-Launcher-[0-9]+\.[0-9]+\.[0-9]-update\.zip\z'){throw 'Invalid update asset name'};$name=$AssetName}
$path=Join-Path $dist $name
if(Test-Path -LiteralPath $path){Remove-Item -LiteralPath $path}
$zip=[IO.Compression.ZipFile]::Open($path,[IO.Compression.ZipArchiveMode]::Create)
try{
 foreach($entry in $items.GetEnumerator()){[IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip,$entry.Value,$entry.Key,[IO.Compression.CompressionLevel]::Optimal)|Out-Null}
 $record=$zip.CreateEntry('update-manifest.json');$stream=$record.Open();try{$stream.Write($signed,0,$signed.Length)}finally{$stream.Dispose()}
}finally{$zip.Dispose()}
& (Join-Path $taskRoot 'build\PackTools.exe') --verify-update $path
if($LASTEXITCODE -ne 0){throw 'Generated update failed production signature/file verification'}
$hash=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($path+'.sha256.txt'),($hash+'  '+$name+[Environment]::NewLine),$utf8)
$exe=Join-Path $dist ('智熄的启动器-'+$version+'-保留数据更新.exe')
& (Join-Path $PSScriptRoot 'build-updater.ps1') -EmbeddedPackage $path -OutputFile $exe
if($BaseUrl){
 $base=[Uri]$BaseUrl;if($base.Scheme -ne 'https' -or $base.UserInfo -or $base.Query -or $base.Fragment){throw 'BaseUrl must be a public HTTPS directory URL'}
 $feed=@{Product='ZhixiLauncher';Format=1;Kind='feed';Version=$version;Notes=$notes;Url=$BaseUrl.TrimEnd('/')+'/'+[Uri]::EscapeDataString($name);Size=(Get-Item -LiteralPath $path).Length;Sha256=$hash}
 [IO.File]::WriteAllBytes((Join-Path $dist 'update.json'),(SignedBytes $feed))
}else{
 Write-Output 'No feed generated: no hosting address configured. Offline ZIP and EXE are ready.'
}
$rsa.Dispose()
Write-Output $path
Write-Output $exe
