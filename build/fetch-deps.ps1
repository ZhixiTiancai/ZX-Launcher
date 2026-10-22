$ErrorActionPreference = 'Stop'
$cache = Join-Path $PSScriptRoot 'cache'
New-Item -ItemType Directory -Force -Path $cache | Out-Null
$items = @(
    @{ Name='PrismLauncher-Windows-MinGW-w64-Portable-11.1.1.zip'; Url='https://github.com/PrismLauncher/PrismLauncher/releases/download/11.1.1/PrismLauncher-Windows-MinGW-w64-Portable-11.1.1.zip'; Hash='05841d0b3bfc0a8212658457cc6035e1c29cd7ffae919464e874e384cbe8d062' },
    @{ Name='PrismLauncher-11.1.1.tar.gz'; Url='https://github.com/PrismLauncher/PrismLauncher/releases/download/11.1.1/PrismLauncher-11.1.1.tar.gz'; Hash='9ea53f93714cd2a59dc0f38153690a59a3f06f439934d1410ec364cc7f363eb5' },
    @{ Name='temurin17-jre.zip'; Url='https://github.com/adoptium/temurin17-binaries/releases/download/jdk-17.0.20.1%2B1/OpenJDK17U-jre_x64_windows_hotspot_17.0.20.1_1.zip'; Hash='bc21a93923103cdaac93ee337b0ae4365e739fde36df823dd456bc67c8a9d352' },
    @{ Name='OpenJDK17U-jdk-sources_17.0.20.1_1.tar.gz'; Url='https://github.com/adoptium/temurin17-binaries/releases/download/jdk-17.0.20.1%2B1/OpenJDK17U-jdk-sources_17.0.20.1_1.tar.gz'; Hash='21e2a065d244ab048e737f21af5d1fc74daaeb6707de36477ead8db1dca71214' },
    @{ Name='temurin21-jre.zip'; Url='https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21.0.12.1%2B1/OpenJDK21U-jre_x64_windows_hotspot_21.0.12.1_1.zip'; Hash='d35f31e712f0fcf6ac5a093edc90204fbff22f720ba3950bd09d331d5e621636' },
    @{ Name='OpenJDK21U-jdk-sources_21.0.12.1_1.tar.gz'; Url='https://github.com/adoptium/temurin21-binaries/releases/download/jdk-21.0.12.1%2B1/OpenJDK21U-jdk-sources_21.0.12.1_1.tar.gz'; Hash='573057d03584ae793fb7ec9a14c76d826d9187a53efeefd99da47403a5308234' }
)
foreach ($item in $items) {
    $dest = Join-Path $cache $item.Name
    if (-not (Test-Path -LiteralPath $dest)) { Invoke-WebRequest -Uri $item.Url -OutFile $dest -TimeoutSec 180 }
    $actual = (Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($item.Hash -and $actual -ne $item.Hash) { throw ('Checksum mismatch: ' + $item.Name) }
    Write-Output ($item.Name + ' SHA256=' + $actual)
}
