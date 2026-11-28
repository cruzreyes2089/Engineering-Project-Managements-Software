; Unified installer source — not authorized to compile or distribute.
; The original dangerous-file security hold remains in effect.
!error "CRC installer release held: independent package authorization and coordinator qualification outstanding"

Unicode true
Name "CRC Practice Manager"
OutFile "CRC-Practice-Manager-Setup.exe"
RequestExecutionLevel user
InstallDir "$LOCALAPPDATA\Programs\CRC Practice Manager"
ShowInstDetails show
Page instfiles

; Generated from exact verified payload inventory, not wildcard inclusion.
; The approved coordinator must reserve a protected extraction directory,
; independently verify its own package and helper identity before execution,
; and bind fresh versus upgrade intent to exact reviewed version identities.
; No InstallDir override, automatic app launch, direct registry writes,
; direct business database access, recursive removal or fallback installer.

!include "reviewed-payload.nsh"
Var CrcStageRoot

Section "CRC Practice Manager"
  ; Remains explicitly non-operational until the coordinator handoff and
  ; package trust chain are implemented and qualified. Do not extract files
  ; or perform registration based solely on the NSIS UI or a build flag.
  Abort "Installation is not authorized."
SectionEnd
