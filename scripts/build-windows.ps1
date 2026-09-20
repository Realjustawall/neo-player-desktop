$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

npm ci
npm run build
python -m pip install --upgrade pip
python -m pip install -r requirements.txt
python -m unittest discover -s tests -v
python python/desktop.py --self-test

Remove-Item -Recurse -Force build, build-ai, dist-win, dist-win-ai, release -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force release | Out-Null
python -m PyInstaller --noconfirm --distpath dist-win --workpath build neo_player.spec
Compress-Archive -Path "dist-win\NEOPlayer\*" -DestinationPath "release\NEO-Player-Portable.zip" -Force

$Nsis = Get-Command makensis -ErrorAction SilentlyContinue
if ($Nsis) { & $Nsis.Source installer.nsi }
elseif (Test-Path "${env:ProgramFiles(x86)}\NSIS\makensis.exe") { & "${env:ProgramFiles(x86)}\NSIS\makensis.exe" installer.nsi }
else { throw "NSIS was not found; installer was not created." }

# The optional AI engine is a separate, first-use download. It is never copied
# into the main portable archive or installer.
python -m pip install -r requirements-ai.txt
python -m PyInstaller --noconfirm --distpath dist-win-ai --workpath build-ai neo_lyrics_ai.spec
Compress-Archive -Path "dist-win-ai\NEOLyricsAI\*" -DestinationPath "release\NEO-Player-Lyrics-AI.zip" -Force
$AiHash = (Get-FileHash "release\NEO-Player-Lyrics-AI.zip" -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "release\NEO-Player-Lyrics-AI.zip.sha256" -Value "$AiHash  NEO-Player-Lyrics-AI.zip" -Encoding ascii

Write-Host "Windows artifacts are available in release\"
