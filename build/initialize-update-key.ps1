$ErrorActionPreference='Stop'
$taskRoot=Split-Path -Parent $PSScriptRoot
$privateFolder=Join-Path $taskRoot '.private'
New-Item -ItemType Directory -Path $privateFolder -Force | Out-Null
$keyPath=Join-Path $privateFolder 'update-signing-key.xml'
$rsa=New-Object System.Security.Cryptography.RSACryptoServiceProvider(3072)
$rsa.PersistKeyInCsp=$false
if(Test-Path -LiteralPath $keyPath){$rsa.FromXmlString([IO.File]::ReadAllText($keyPath))}
else{if(Test-Path -LiteralPath (Join-Path $taskRoot 'src\UpdateTrust.cs')){throw 'Existing public key found. Restore the original private signing key instead of creating a replacement.'};[IO.File]::WriteAllText($keyPath,$rsa.ToXmlString($true),(New-Object Text.UTF8Encoding($false)))}
$source='namespace FoolLauncher { internal static class UpdateTrust { public const string PublicKey = @"'+$rsa.ToXmlString($false)+'"; } }'
[IO.File]::WriteAllText((Join-Path $taskRoot 'src\UpdateTrust.cs'),$source,(New-Object Text.UTF8Encoding($false)))
$rsa.Dispose()
Write-Output 'Update public key prepared. Keep .private/update-signing-key.xml on the publisher computer only.'
