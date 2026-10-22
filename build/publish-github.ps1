param([string]$Repository='ZhixiTiancai/ZX-Launcher',[string]$Gh='gh',[switch]$Publish)
$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $taskRoot
$version=[regex]::Match([IO.File]::ReadAllText((Join-Path $taskRoot 'src\Program.cs')),'AssemblyVersion\("(\d+\.\d+\.\d+)\.\d+"\)').Groups[1].Value
$tag='v'+$version
$folder=Join-Path $taskRoot ('dist\github-'+$tag)
$assets=@('ZX-Launcher-Windows-x64.zip',('ZX-Launcher-'+$version+'-update.zip'),('ZX-Launcher-'+$version+'-update.exe'),'update.json','SHA256SUMS.txt')
foreach($asset in $assets){if(-not(Test-Path -LiteralPath (Join-Path $folder $asset))){throw 'Prepare the release first: missing '+$asset}}
$notes=Join-Path $taskRoot 'release-notes.md'
Write-Output ('Repository: '+$Repository+'; release: '+$tag)
Write-Output ('Assets: '+($assets -join ', '))
if(-not $Publish){Write-Output 'Review only. Add -Publish to upload and publish the release.';exit 0}
$previousHttpsProxy=$env:HTTPS_PROXY
$previousHttpProxy=$env:HTTP_PROXY
try {
 if(-not $env:HTTPS_PROXY){$target=[Uri]'https://github.com';$proxy=[Net.WebRequest]::GetSystemWebProxy().GetProxy($target);if($proxy -and $proxy.AbsoluteUri -ne $target.AbsoluteUri){$env:HTTPS_PROXY=$proxy.AbsoluteUri;$env:HTTP_PROXY=$proxy.AbsoluteUri}}
& $Gh auth status
if($LASTEXITCODE -ne 0){throw 'Sign in with gh auth login --web first. Do not put tokens in source files.'}
if(-not(Test-Path -LiteralPath (Join-Path $taskRoot '.git'))){throw 'Initialize and push the public source repository first; never add .private, release or dist to Git'}
$dirty=& git status --porcelain --untracked-files=no
if($LASTEXITCODE -ne 0 -or $dirty){throw 'Commit the tracked source changes before publishing'}
& git push origin HEAD
if($LASTEXITCODE -ne 0){throw 'Source push failed'}
$head=(& git rev-parse HEAD).Trim()
$existing=& $Gh release view $tag --repo $Repository --json isDraft --jq '.isDraft' 2>$null
if($LASTEXITCODE -eq 0){if($existing -ne 'true'){throw 'This version is already published. Use a new version; do not replace published update packages.'}}
else{& $Gh release create $tag --repo $Repository --target $head --title ('智熄的启动器 '+$version) --notes-file $notes --draft;if($LASTEXITCODE -ne 0){throw 'Unable to create release draft'}}
$paths=@($assets|ForEach-Object{Join-Path $folder $_})
& $Gh release upload $tag @paths --repo $Repository --clobber
if($LASTEXITCODE -ne 0){throw 'Asset upload incomplete. Release remains a draft; rerun to resume.'}
$remote=(& $Gh release view $tag --repo $Repository --json assets | ConvertFrom-Json).assets
foreach($asset in $assets){$match=@($remote|Where-Object{$_.name -eq $asset});if($match.Count -ne 1 -or $match[0].size -ne (Get-Item -LiteralPath (Join-Path $folder $asset)).Length){throw ('Uploaded asset verification failed: '+$asset)}}
& $Gh release edit $tag --repo $Repository --draft=false --latest
if($LASTEXITCODE -ne 0){throw 'Release publication failed'}
Write-Output ('Published: https://github.com/'+$Repository+'/releases/tag/'+$tag)

} finally { $env:HTTPS_PROXY=$previousHttpsProxy;$env:HTTP_PROXY=$previousHttpProxy }
