$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

npm ci
npm run build
python -m pip install --upgrade pip
python -m pip install -r requirements-ai.txt
python -m unittest discover -s tests -v
python python/desktop.py --self-test

Remove-Item -Recurse -Force build, dist-win, release -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force release | Out-Null
python -m PyInstaller --noconfirm --distpath dist-win --workpath build neo_player.spec
Compress-Archive -Path "dist-win\NEOPlayer\*" -DestinationPath "release\NEO-Player-Portable.zip" -Force

$Nsis = Get-Command makensis -ErrorAction SilentlyContinue
if ($Nsis) { & $Nsis.Source installer.nsi }
elseif (Test-Path "${env:ProgramFiles(x86)}\NSIS\makensis.exe") { & "${env:ProgramFiles(x86)}\NSIS\makensis.exe" installer.nsi }
else { throw "NSIS was not found; installer was not created." }

Write-Host "Windows artifacts are available in release\"
