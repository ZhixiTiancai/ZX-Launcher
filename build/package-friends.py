"""Package the launcher only, using original dependency archives as allow lists."""
from pathlib import Path, PurePosixPath
import hashlib
import json
import re
import zipfile

workspace = Path(__file__).resolve().parent.parent
release = workspace / 'release' / 'FoolLauncher'
dist = workspace / 'dist'
dist.mkdir(exist_ok=True)
version = re.search(r'AssemblyVersion\("(\d+\.\d+\.\d+)\.\d+"\)', (workspace / 'src/Program.cs').read_text(encoding='utf-8-sig')).group(1)
target = dist / f'智熄的启动器-{version}-朋友分享版-不含整合包-Windows64.zip'
files = {}
generated = {}

def digest(stream):
    return hashlib.file_digest(stream, 'sha256').hexdigest()

def add(source, name):
    if not source.is_file():
        raise RuntimeError('Required file missing: ' + str(source))
    if name in files or name in generated:
        raise RuntimeError('Duplicate archive entry: ' + name)
    files[name] = source

def original_files(archive_path, prefix):
    """Do not include anything created by running a bundled dependency."""
    with zipfile.ZipFile(archive_path) as original:
        for entry in original.infolist():
            if entry.is_dir():
                continue
            relative = PurePosixPath(entry.filename)
            if relative.is_absolute() or '..' in relative.parts:
                raise RuntimeError('Invalid dependency archive path')
            source = release / prefix / Path(*relative.parts)
            with original.open(entry) as expected, source.open('rb') as actual:
                if digest(expected) != digest(actual):
                    raise RuntimeError('Dependency changed from original: ' + str(source))
            add(source, prefix + '/' + relative.as_posix())

add(release / '智熄的启动器.exe', '智熄的启动器.exe')
add(release / 'App.xaml', 'App.xaml')
add(release / '智熄更新器.exe', '智熄更新器.exe')
# Only the original, pinned Microsoft WebView2 SDK binaries/license are distributed.
with zipfile.ZipFile(workspace / 'build/webview2/microsoft.web.webview2.1.0.4258.31.nupkg') as sdk:
    for entry, name in {'lib/net462/Microsoft.Web.WebView2.Core.dll':'Microsoft.Web.WebView2.Core.dll', 'lib/net462/Microsoft.Web.WebView2.Wpf.dll':'Microsoft.Web.WebView2.Wpf.dll', 'runtimes/win-x64/native/WebView2Loader.dll':'WebView2Loader.dll', 'LICENSE.txt':'WebView2-LICENSE.txt'}.items():
        assert hashlib.sha256(sdk.read(entry)).digest() == hashlib.sha256((release / name).read_bytes()).digest()
        add(release / name, name)

original_files(workspace / 'build/cache/PrismLauncher-Windows-MinGW-w64-Portable-11.1.1.zip', 'engine')
original_files(workspace / 'build/cache/temurin17-jre.zip', 'runtime')
original_files(workspace / 'build/cache/temurin21-jre.zip', 'runtime')
for name, sha in {
    'PrismLauncher-11.1.1.tar.gz': '9ea53f93714cd2a59dc0f38153690a59a3f06f439934d1410ec364cc7f363eb5',
    'OpenJDK17U-jdk-sources_17.0.20.1_1.tar.gz': '21e2a065d244ab048e737f21af5d1fc74daaeb6707de36477ead8db1dca71214',
    'OpenJDK21U-jdk-sources_21.0.12.1_1.tar.gz': '573057d03584ae793fb7ec9a14c76d826d9187a53efeefd99da47403a5308234',
}.items():
    source = release / 'third-party/source' / name
    with source.open('rb') as stream:
        if digest(stream) != sha:
            raise RuntimeError('Third-party source checksum failed: ' + name)
    add(source, 'third-party/source/' + name)

# Only a stock language file is copied from data. No instance/cache/account directory is copied.
translation = release / 'data/translations/mmc_zh.qm'
assert hashlib.sha1(translation.read_bytes()).hexdigest() == '0bfc1a6c74c387ddb5aabf9de977c861a347d141'
add(translation, 'data/translations/mmc_zh.qm')
guide = (workspace / '朋友版说明.txt').read_bytes()
generated['先看这里-启动与导入说明.txt'] = guide
generated['使用说明.txt'] = guide
notice = (workspace / 'THIRD-PARTY.txt').read_text(encoding='utf-8')
notice = notice.replace(
    "The user-provided modpack, mods, resource packs, configs, scripts and saves retain their original authors' licenses.\n"
    "This archive is assembled for the requesting user's personal use from their supplied modpack.\n",
    "This launcher-only distribution includes no user-provided modpacks or game content.\n"
)
notice += '\nThis launcher-only distribution contains no modpacks, Minecraft game binaries, user accounts, tokens, saves, or usage logs.\n'
generated['THIRD-PARTY.txt'] = notice.encode('utf-8')
generated['source/THIRD-PARTY.txt'] = notice.encode('utf-8')
generated['source/使用说明.txt'] = guide
generated['source/朋友版说明.txt'] = guide
generated['source/README.md'] = (workspace / 'README.md').read_bytes()
add(workspace / 'LICENSE', 'source/LICENSE')
add(workspace / '发布更新说明.md', 'source/发布更新说明.md')
add(workspace / 'release-notes.md', 'source/release-notes.md')
for source in (workspace / 'src').rglob('*'):
    if source.is_file():
        add(source, 'source/src/' + source.relative_to(workspace / 'src').as_posix())
for name in ['prepare-github-release.ps1', 'publish-github.ps1', 'set-version.ps1', 'build.ps1', 'build-updater.ps1', 'UpdaterProgram.cs', 'package-program-update.ps1', 'initialize-update-key.ps1', 'install-update.ps1', 'fetch-webview.ps1', 'fetch-deps.ps1', 'assemble.ps1', 'package-friends.py']:
    add(workspace / 'build' / name, 'source/build/' + name)

names = set(files) | set(generated)
for name in names:
    lower = name.lower()
    if any(part in lower for part in ['accounts.json','prismlauncher.cfg','launcher-instance.json','pack-index.json','missing-files','/saves/','/mods/','/logs/','updates-backup','zhixi-account-settings.json','account-backups','/exports/','instance-icons','skin-browser','update-signing-key.xml','.private','launcher-update-settings.json']):
        raise RuntimeError('Unexpected instance or private data: ' + name)
    if lower.startswith('data/') and name != 'data/translations/mmc_zh.qm':
        raise RuntimeError('Unexpected data file: ' + name)
print('Verified original dependencies. Packing launcher-only files:', len(names), flush=True)
with zipfile.ZipFile(target, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
    # EXE is at ZIP root, not hidden inside another directory.
    archive.write(files.pop('智熄的启动器.exe'), '智熄的启动器.exe')
    for name, source in sorted(files.items()):
        archive.write(source, name)
    for name, content in generated.items():
        archive.writestr(name, content)
with zipfile.ZipFile(target) as archive:
    assert archive.testzip() is None, 'ZIP CRC failed'
    assert archive.namelist()[0] == '智熄的启动器.exe'
    assert not any(n.startswith('data/instances/') for n in archive.namelist())
    assert 'engine/prismlauncher.exe' in archive.namelist()
    assert any(n.startswith('runtime/') and n.endswith('/bin/javaw.exe') for n in archive.namelist())
with target.open('rb') as stream:
    sha = digest(stream)
target.with_suffix('.zip.sha256.txt').write_text(sha + '  ' + target.name + '\n', encoding='utf-8')
report = dict(path=str(target), bytes=target.stat().st_size, sha256=sha, entries=len(names), crc='PASS',
              version=version, exeAtRoot=True, modpacksIncluded=False, userDataIncluded=False, dependencyIntegrity='PASS')
target.with_suffix('.verification.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(report, ensure_ascii=False), flush=True)
