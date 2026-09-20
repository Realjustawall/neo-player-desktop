$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $PSScriptRoot
Set-Location $Root

npm ci
npm run build

if (!(Test-Path "dist/neo-logo.webp")) { throw "NEO logo asset was not copied to dist" }
$logoBytes = [System.IO.File]::ReadAllBytes((Resolve-Path "dist/neo-logo.webp"))
if ($logoBytes.Length -lt 12 -or [Text.Encoding]::ASCII.GetString($logoBytes,0,4) -ne "RIFF" -or [Text.Encoding]::ASCII.GetString($logoBytes,8,4) -ne "WEBP") { throw "NEO logo asset is invalid" }
$iconBytes = [System.IO.File]::ReadAllBytes((Resolve-Path "public/neo-player.ico"))
if ($iconBytes.Length -lt 4 -or $iconBytes[0] -ne 0 -or $iconBytes[1] -ne 0 -or $iconBytes[2] -ne 1 -or $iconBytes[3] -ne 0) { throw "Windows icon file is invalid" }

python -m pip install --upgrade pip
python -m pip install -r requirements.txt
python -m unittest discover -s tests -v
python python/desktop.py --self-test

Remove-Item -Recurse -Force build, build-ai, dist-win, dist-win-ai, release -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force release | Out-Null
python -m PyInstaller --noconfirm --distpath dist-win --workpath build neo_player.spec
if (!(Test-Path "dist-win/NEOPlayer/NEOPlayer.exe")) { throw "NEOPlayer.exe was not created" }
Compress-Archive -Path "dist-win\NEOPlayer\*" -DestinationPath "release\NEO-Player-Portable.zip" -Force

$Nsis = Get-Command makensis -ErrorAction SilentlyContinue
if ($Nsis) { & $Nsis.Source installer.nsi }
elseif (Test-Path "${env:ProgramFiles(x86)}\NSIS\makensis.exe") { & "${env:ProgramFiles(x86)}\NSIS\makensis.exe" installer.nsi }
else { throw "NSIS was not found; installer was not created." }

python -m pip install -r requirements-ai.txt
python -m PyInstaller --noconfirm --distpath dist-win-ai --workpath build-ai neo_lyrics_ai.spec
& "dist-win-ai\NEOLyricsAI\NEOLyricsAI.exe" '{"action":"self-test"}'
if ($LASTEXITCODE -ne 0) { throw "Optional media/AI engine smoke test failed." }
Compress-Archive -Path "dist-win-ai\NEOLyricsAI\*" -DestinationPath "release\NEO-Player-Lyrics-AI.zip" -Force
$AiHash = (Get-FileHash "release\NEO-Player-Lyrics-AI.zip" -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -Path "release\NEO-Player-Lyrics-AI.zip.sha256" -Value "$AiHash  NEO-Player-Lyrics-AI.zip" -Encoding ascii

Write-Host "Windows artifacts are available in release\"