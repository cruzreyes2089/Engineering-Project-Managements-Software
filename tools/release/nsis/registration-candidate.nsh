; Source-only registration operations. No standalone installer.
; The eventual reviewed coordinator MUST hold the program lease, verify the
; installed inventory, confirm production startup is authorized, and establish
; that the fixed registry key and shortcut are absent before calling this macro.
; A failed or interrupted registration must keep its pending marker.
; No automatic application launch or registry-key removal.
; Caller variables are set by that coordinator, never by command-line input:
; $CrcActiveExe, $CrcVersionDirectory, $CrcUninstaller, $CrcRegistrationReady.
!macro CRC_RegisterFreshInstall
  StrCmp $CrcRegistrationReady "verified-fresh" +2
  Abort "Reviewed installation registration required."
  SetShellVarContext current
  SetRegView 64
  StrCmp $CrcUninstaller "$LOCALAPPDATA\Programs\CRC Practice Manager\CRC.Uninstall.exe" +2
  Abort "Unexpected uninstaller location."
  StrCmp $CrcActiveExe "$CrcVersionDirectory\CRC.PracticeManager.exe" +2
  Abort "Unexpected application location."
  IfFileExists "$CrcActiveExe" +2 0
  Abort "Selected application missing."
  IfFileExists "$CrcUninstaller" +2 0
  Abort "Uninstaller missing."
  IfFileExists "$SMPROGRAMS\CRC Practice Manager.lnk" 0 +2
  Abort "Existing shortcut requires ownership review."
  ClearErrors
  SetOutPath "$CrcVersionDirectory"
  IfErrors 0 +2
  Abort "Selected program directory unavailable."
  ClearErrors
  CreateShortCut "$SMPROGRAMS\CRC Practice Manager.lnk" "$CrcActiveExe" "" "$CrcActiveExe" 0 SW_SHOWNORMAL
  IfErrors 0 +2
  Abort "Start menu registration requires review."
  ; Fixed product-specific key, current user only.
  ClearErrors
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CRC.PracticeManager" "DisplayName" "CRC Practice Manager"
  IfErrors 0 +2
  Abort "Application registration requires review."
  ClearErrors
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CRC.PracticeManager" "Publisher" "CRC Engineering"
  IfErrors 0 +2
  Abort "Application registration requires review."
  ClearErrors
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CRC.PracticeManager" "InstallLocation" "$LOCALAPPDATA\Programs\CRC Practice Manager"
  IfErrors 0 +2
  Abort "Application registration requires review."
  ClearErrors
  WriteRegStr HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CRC.PracticeManager" "UninstallString" '$\"$CrcUninstaller$\"'
  IfErrors 0 +2
  Abort "Application registration requires review."
  ClearErrors
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CRC.PracticeManager" "NoModify" 1
  IfErrors 0 +2
  Abort "Application registration requires review."
  ClearErrors
  WriteRegDWORD HKCU "Software\Microsoft\Windows\CurrentVersion\Uninstall\CRC.PracticeManager" "NoRepair" 1
  IfErrors 0 +2
  Abort "Application registration requires review."
  ; Coordinator must read back exact registry values and shortcut target,
  ; verify ownership evidence, then clear the registration pending record.
!macroend
