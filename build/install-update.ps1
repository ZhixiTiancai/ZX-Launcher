param([Parameter(Mandatory=$true)][string]$TargetRoot,[switch]$ProgramOnly)
$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $taskRoot 'release\FoolLauncher'
$target = [System.IO.Path]::GetFullPath($TargetRoot)
if (-not (Test-Path -LiteralPath (Join-Path $target 'engine\prismlauncher.exe'))) { throw '不是已有的愚者启动器目录' }
$running = Get-Process -ErrorAction SilentlyContinue | Where-Object {
    if ($ProgramOnly) { $_.Path -and $_.Path.Equals((Join-Path $target '愚者启动器.exe'), [StringComparison]::OrdinalIgnoreCase) -or $_.Path -and $_.Path.Equals((Join-Path $target '智熄的启动器.exe'), [StringComparison]::OrdinalIgnoreCase) }
    else { $_.Path -and $_.Path.StartsWith($target + '\', [StringComparison]::OrdinalIgnoreCase) }
}
if ($running) { throw '请先退出此目录中的启动器与游戏，再更新。' }
$backup = Join-Path $target ('updates-backup\' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $backup -Force | Out-Null
$relativeFiles = @('智熄的启动器.exe','智熄更新器.exe','App.xaml','使用说明.txt','THIRD-PARTY.txt','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.Wpf.dll','WebView2Loader.dll','WebView2-LICENSE.txt')
if (-not $ProgramOnly) {
    $relativeFiles += @('third-party\source\OpenJDK21U-jdk-sources_21.0.12.1_1.tar.gz')
    Get-ChildItem -LiteralPath (Join-Path $source 'runtime\jdk-21.0.12.1+1-jre') -Recurse -File | ForEach-Object { $relativeFiles += $_.FullName.Substring($source.Length + 1) }
}
foreach ($relative in $relativeFiles) {
    $from = Join-Path $source $relative
    $to = Join-Path $target $relative
    if (Test-Path -LiteralPath $to) {
        $old = Join-Path $backup $relative
        New-Item -ItemType Directory -Path (Split-Path -Parent $old) -Force | Out-Null
        Copy-Item -LiteralPath $to -Destination $old -Force
    }
    New-Item -ItemType Directory -Path (Split-Path -Parent $to) -Force | Out-Null
    Copy-Item -LiteralPath $from -Destination $to -Force
}
$legacy = Join-Path $target '愚者启动器.exe'
if (Test-Path -LiteralPath $legacy) {
    # Move only this explicitly named file into the backup after the new EXE is installed.
    Move-Item -LiteralPath $legacy -Destination (Join-Path $backup '愚者启动器.exe')
}
New-Item -ItemType Directory -Path (Join-Path $target 'source\src') -Force | Out-Null
Copy-Item -Path (Join-Path $taskRoot 'src\*') -Destination (Join-Path $target 'source\src') -Recurse -Force
New-Item -ItemType Directory -Path (Join-Path $target 'source\build') -Force | Out-Null
foreach ($name in @('build.ps1','build-updater.ps1','UpdaterProgram.cs','package-program-update.ps1','initialize-update-key.ps1','fetch-webview.ps1','fetch-deps.ps1','assemble.ps1','package-friends.py','package-compat-update.py','package-ui-update.py')) {
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot $name) -Destination (Join-Path $target 'source\build') -Force
}
Write-Output ('Updated: ' + $target)
Write-Output ('Backup: ' + $backup)
