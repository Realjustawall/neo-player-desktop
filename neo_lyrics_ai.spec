from PyInstaller.utils.hooks import collect_all

datas = []
binaries = []
hiddenimports = []
for package in ('faster_whisper', 'ctranslate2', 'tokenizers', 'av'):
    package_datas, package_binaries, package_hidden = collect_all(package)
    datas += package_datas
    binaries += package_binaries
    hiddenimports += package_hidden

a = Analysis(
    ['python/lyrics_worker.py'], pathex=['python'], binaries=binaries, datas=datas,
    hiddenimports=hiddenimports, excludes=['tkinter', 'webview', 'pytest'], noarchive=False,
)
pyz = PYZ(a.pure)
exe = EXE(
    pyz, a.scripts, [], exclude_binaries=True, name='NEOLyricsAI', debug=False,
    bootloader_ignore_signals=False, strip=False, upx=True, console=True,
)
coll = COLLECT(exe, a.binaries, a.datas, strip=False, upx=True, name='NEOLyricsAI')
