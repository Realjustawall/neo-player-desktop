Unicode true
Name "NEO Player"
OutFile "release\NEO-Player-Setup.exe"
InstallDir "$LOCALAPPDATA\Programs\NEO Player"
RequestExecutionLevel user

Page directory
Page instfiles
UninstPage uninstConfirm
UninstPage instfiles

Section "Install"
  SetOutPath "$INSTDIR"
  File /r "dist-win\NEOPlayer\*"
  CreateDirectory "$SMPROGRAMS\NEO Player"
  CreateShortcut "$SMPROGRAMS\NEO Player\NEO Player.lnk" "$INSTDIR\NEOPlayer.exe"
  CreateShortcut "$DESKTOP\NEO Player.lnk" "$INSTDIR\NEOPlayer.exe"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\NEOPlayer" "DisplayName" "NEO Player"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\NEOPlayer" "DisplayVersion" "0.3.1"
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\NEOPlayer" "UninstallString" "$\"$INSTDIR\Uninstall.exe$\""
SectionEnd

Section "Uninstall"
  Delete "$DESKTOP\NEO Player.lnk"
  RMDir /r "$SMPROGRAMS\NEO Player"
  RMDir /r "$INSTDIR"
  DeleteRegKey HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\NEOPlayer"
SectionEnd
