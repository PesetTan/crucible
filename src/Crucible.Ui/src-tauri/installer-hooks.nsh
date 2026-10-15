; Program files and retained simulation data must never share a removal root.
!macro NSIS_HOOK_PREINSTALL
  StrCmp $INSTDIR "$LOCALAPPDATA\Crucible" crucible_data_conflict
  IfFileExists "$INSTDIR\catalog.sqlite" crucible_data_conflict
  IfFileExists "$INSTDIR\data\simulator.sqlite" crucible_data_conflict
  Goto crucible_data_ok
  crucible_data_conflict:
    MessageBox MB_ICONSTOP|MB_OK "Choose a program folder separate from your Crucible user-data folder." /SD IDOK
    Abort
  crucible_data_ok:
!macroend
