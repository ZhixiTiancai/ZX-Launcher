$ErrorActionPreference = 'Stop'
$taskRoot = Split-Path -Parent $PSScriptRoot
$cache = Join-Path $PSScriptRoot 'webview2'
New-Item -ItemType Directory -Path $cache -Force | Out-Null
$package = Join-Path $cache 'microsoft.web.webview2.1.0.4258.31.nupkg'
if (-not (Test-Path -LiteralPath $package)) {
    [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
    Invoke-WebRequest -Uri 'https://api.nuget.org/v3-flatcontainer/microsoft.web.webview2/1.0.4258.31/microsoft.web.webview2.1.0.4258.31.nupkg' -OutFile $package
}
if ((Get-FileHash -LiteralPath $package -Algorithm SHA256).Hash -ne '56F7F4B8BF9AEE4B8EFEFBBDD4F67D5F74EBD1B100ED0806DA71BF76AF481AA9') { throw 'WebView2 package checksum mismatch' }
Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($package)
try {
    $output = Join-Path $taskRoot 'release\FoolLauncher'
    New-Item -ItemType Directory -Path $output -Force | Out-Null
    $entries = @{'lib/net462/Microsoft.Web.WebView2.Core.dll'='Microsoft.Web.WebView2.Core.dll';'lib/net462/Microsoft.Web.WebView2.Wpf.dll'='Microsoft.Web.WebView2.Wpf.dll';'runtimes/win-x64/native/WebView2Loader.dll'='WebView2Loader.dll';'LICENSE.txt'='WebView2-LICENSE.txt'}
    foreach ($name in $entries.Keys) {
        $entry = $archive.GetEntry($name)
        if (-not $entry) { throw ('Missing WebView2 package entry: ' + $name) }
        $to = Join-Path $output $entries[$name]
        if (Test-Path -LiteralPath $to) {
            # Avoid opening an unchanged loaded DLL for writing.
            $inputStream = $entry.Open()
            $sha = [Security.Cryptography.SHA256]::Create()
            try { $expected = [BitConverter]::ToString($sha.ComputeHash($inputStream)).Replace('-','') } finally { $sha.Dispose(); $inputStream.Dispose() }
            if ((Get-FileHash -LiteralPath $to -Algorithm SHA256).Hash -eq $expected) { continue }
        }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,$to,$true)
    }
    foreach ($name in @('Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.Wpf.dll','WebView2Loader.dll')) { Copy-Item -LiteralPath (Join-Path $output $name) -Destination (Join-Path $PSScriptRoot $name) -Force }
} finally { $archive.Dispose() }
