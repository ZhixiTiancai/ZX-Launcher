"""Build the Fabric/Java21 update without including any user data."""
from pathlib import Path
import hashlib
import json
import zipfile

workspace = Path(__file__).resolve().parent.parent
release = workspace / 'release/FoolLauncher'
target = workspace / 'dist/愚者启动器-0.1.3-Fabric与Java21修复更新包.zip'
items = {}
for name in ['愚者启动器.exe', 'App.xaml', '使用说明.txt', 'THIRD-PARTY.txt']:
    items[name] = release / name
with zipfile.ZipFile(workspace / 'build/cache/temurin21-jre.zip') as dependency:
    for entry in dependency.infolist():
        if entry.is_dir():
            continue
        assert '..' not in Path(entry.filename).parts
        source = release / 'runtime' / entry.filename
        with dependency.open(entry) as original, source.open('rb') as actual:
            assert hashlib.file_digest(original, 'sha256').digest() == hashlib.file_digest(actual, 'sha256').digest()
        items['runtime/' + entry.filename] = source
source_name = 'third-party/source/OpenJDK21U-jdk-sources_21.0.12.1_1.tar.gz'
with (release / source_name).open('rb') as stream:
    assert hashlib.file_digest(stream, 'sha256').hexdigest() == '573057d03584ae793fb7ec9a14c76d826d9187a53efeefd99da47403a5308234'
items[source_name] = release / source_name
for source in (workspace / 'src').glob('*'):
    if source.is_file():
        items['source/src/' + source.name] = source
items['source/LICENSE'] = workspace / 'LICENSE'
for name in ['build.ps1', 'fetch-deps.ps1', 'assemble.ps1', 'package-friends.py', 'package-compat-update.py']:
    items['source/build/' + name] = workspace / 'build' / name
notes = '''愚者启动器 0.1.3 — Fabric / Java 21 兼容修复

修复 Fabric / Quilt 映射组件未指定版本时被误报“无效的组件标识”。
按已选 Minecraft 版本补全映射组件，并继续验证官方组件 SHA-256 哈希链。
增加 Fabric Maven 库的下载、仓库 SHA-1 与文件大小校验，写入 Prism 正常缓存。
附带原版 Eclipse Temurin Java 21，启动时按游戏组件要求选择 Java 17 / 21。
保留在 Prism 实例设置中手动选择的外部 Java。

更新方法：
1. 退出愚者启动器、Prism 和游戏。
2. 将此 ZIP 完整解压到旧版“愚者启动器.exe”所在目录，同名文件覆盖。
   本包是更新包，需要原有的 engine 与 Java 17 runtime；不能单独运行。
3. 双击“愚者启动器.exe”，确认版本为 0.1.3。
4. 选择已导入的“方可梦空岛3.1修复版”，点击“启动游戏”。无需重新导入。

本更新不包含整合包、Minecraft 本体、账号、令牌、存档或日志。
首次启动需要联网补全相应版本游戏依赖。
'''
with zipfile.ZipFile(target, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
    for name, source in sorted(items.items()):
        assert not name.startswith('data/')
        archive.write(source, name)
    archive.writestr('更新说明-0.1.3.txt', notes.encode('utf-8-sig'))
with zipfile.ZipFile(target) as archive:
    assert archive.testzip() is None
with target.open('rb') as stream:
    sha = hashlib.file_digest(stream, 'sha256').hexdigest()
target.with_suffix('.zip.sha256.txt').write_text(sha + '  ' + target.name + '\n', encoding='utf-8')
report = dict(path=str(target), bytes=target.stat().st_size, sha256=sha, crc='PASS', userDataIncluded=False)
target.with_suffix('.verification.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(report, ensure_ascii=False))
