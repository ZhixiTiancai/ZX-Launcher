from pathlib import Path
import hashlib
import json
import zipfile

workspace = Path(__file__).resolve().parent.parent
release = workspace / 'release/FoolLauncher'
target = workspace / 'dist/智熄的启动器-0.1.4-改名功能更新包.zip'
items = {name: release / name for name in ['智熄的启动器.exe', 'App.xaml', '使用说明.txt', 'THIRD-PARTY.txt']}
items['source/LICENSE'] = workspace / 'LICENSE'
for source in (workspace / 'src').glob('*'):
    if source.is_file():
        items['source/src/' + source.name] = source
for name in ['build.ps1', 'fetch-deps.ps1', 'assemble.ps1', 'package-friends.py', 'package-ui-update.py', 'install-update.ps1']:
    items['source/build/' + name] = workspace / 'build' / name
notes = '''智熄的启动器 0.1.4

启动器名称改为“智熄的启动器”，EXE 文件名也已更新。
选中整合包后，点击右侧标题旁的铅笔按钮，即可直接编辑名称；Enter / 勾号保存，Esc / 叉号取消。
名称同步到左侧列表与 Prism 实例设置，重启后仍然保留。
改名不移动实例文件夹，也不修改游戏文件、存档或整合包清单。

旧版更新：
1. 退出启动器、Prism 和游戏。
2. 把更新包完整解压到原启动器目录，同名文件覆盖。
3. 以后请打开“智熄的启动器.exe”。旧“愚者启动器.exe”可移走。
4. 不需要重新导入整合包。

此更新包适用于已有的 0.1.3（需要原来的 engine / runtime），不能独立运行。
首次使用或给朋友分享，请使用“不含整合包”的完整便携包。
本包不含整合包、账号、令牌、日志或存档。
'''
with zipfile.ZipFile(target, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as archive:
    for name, source in sorted(items.items()):
        assert not name.startswith('data/')
        archive.write(source, name)
    archive.writestr('更新说明-0.1.4.txt', notes.encode('utf-8-sig'))
with zipfile.ZipFile(target) as archive:
    assert archive.testzip() is None
with target.open('rb') as stream:
    sha = hashlib.file_digest(stream, 'sha256').hexdigest()
target.with_suffix('.zip.sha256.txt').write_text(sha + '  ' + target.name + '\n', encoding='utf-8')
report = dict(path=str(target), bytes=target.stat().st_size, sha256=sha, crc='PASS', userDataIncluded=False)
target.with_suffix('.verification.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(report, ensure_ascii=False))
