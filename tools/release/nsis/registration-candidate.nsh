; Validation-only placeholder. Registration must use the reviewed native
; coordinator as the single authority. No direct registry/shortcut writes.
; Keep this macro unusable until an approved executable bridge exists.
!macro CRC_RegisterFreshInstall
  Abort "Native registration coordinator not wired; release held."
!macroend
