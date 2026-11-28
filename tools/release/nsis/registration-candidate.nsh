; Validation-only placeholder. The reviewed managed coordinator is the
; single registration authority. No direct registry or shortcut writes.
; Keep this macro unusable until the independently authorized bridge exists.
!macro CRC_RegisterFreshInstall
  Abort "Native registration coordinator not wired; release held."
!macroend
