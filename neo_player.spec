from PyInstaller.utils.hooks import collect_all, collect_submodules

datas = [('dist', 'dist'), ('public/neo-mark.svg', 'public')]
hiddenimports = ['webview.platforms.edgechromium', 'webview.platforms.winforms']
binaries = []

for package in ('mutagen', 'bottle', 'faster_whisper', 'ctranslate2', 'tokenizers', 'av'):
    try:
        package_datas, package_binaries, package_hidden = collect_all(package)
        datas += package_datas
        hiddenimports += package_hidden
    except Exception:
        package_binaries = []
    binaries += package_binaries

hiddenimports += collect_submodules('mutagen')

a = Analysis(
    ['python/desktop.py'],
    pathex=['python'],
    binaries=binaries,
    datas=datas,
    hiddenimports=hiddenimports,
    excludes=['tkinter', 'pytest'],
    noarchive=False,
)
pyz = PYZ(a.pure)
exe = EXE(
    pyz, a.scripts, [], exclude_binaries=True,
    name='NEOPlayer', debug=False, bootloader_ignore_signals=False,
    strip=False, upx=True, console=False,
)
coll = COLLECT(
    exe, a.binaries, a.datas, strip=False, upx=True,
    name='NEOPlayer',
)
