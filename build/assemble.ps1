$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $taskRoot 'release\FoolLauncher'
Add-Type -AssemblyName System.IO.Compression.FileSystem
if (-not (Test-Path -LiteralPath (Join-Path $output 'engine\prismlauncher.exe'))) {
    [System.IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $PSScriptRoot 'cache\PrismLauncher-Windows-MinGW-w64-Portable-11.1.1.zip'), (Join-Path $output 'engine'))
}
if (-not (Test-Path -LiteralPath (Join-Path $output 'runtime\jdk-17.0.20.1+1-jre\bin\javaw.exe'))) {
    [System.IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $PSScriptRoot 'cache\temurin17-jre.zip'), (Join-Path $output 'runtime'))
}
if (-not (Test-Path -LiteralPath (Join-Path $output 'runtime\jdk-21.0.12.1+1-jre\bin\javaw.exe'))) {
    [System.IO.Compression.ZipFile]::ExtractToDirectory((Join-Path $PSScriptRoot 'cache\temurin21-jre.zip'), (Join-Path $output 'runtime'))
}
New-Item -ItemType Directory -Force -Path (Join-Path $output 'third-party\source') | Out-Null
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'cache\PrismLauncher-11.1.1.tar.gz') -Destination (Join-Path $output 'third-party\source') -Force
$javaSource = Join-Path $PSScriptRoot 'cache\OpenJDK17U-jdk-sources_17.0.20.1_1.tar.gz'
if (Test-Path -LiteralPath $javaSource) { Copy-Item -LiteralPath $javaSource -Destination (Join-Path $output 'third-party\source') -Force }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'cache\OpenJDK21U-jdk-sources_21.0.12.1_1.tar.gz') -Destination (Join-Path $output 'third-party\source') -Force
Copy-Item -LiteralPath (Join-Path $taskRoot '使用说明.txt') -Destination $output -Force
Copy-Item -LiteralPath (Join-Path $taskRoot 'THIRD-PARTY.txt') -Destination $output -Force
New-Item -ItemType Directory -Force -Path (Join-Path $output 'source') | Out-Null
Copy-Item -LiteralPath (Join-Path $taskRoot 'src') -Destination (Join-Path $output 'source') -Recurse -Force
New-Item -ItemType Directory -Force -Path (Join-Path $output 'source\build') | Out-Null
Get-ChildItem -LiteralPath $PSScriptRoot -File | Where-Object Extension -In '.ps1' | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $output 'source\build') -Force
}
Copy-Item -LiteralPath (Join-Path $taskRoot 'LICENSE') -Destination (Join-Path $output 'source\LICENSE') -Force
