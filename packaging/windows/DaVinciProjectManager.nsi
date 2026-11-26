Unicode true
RequestExecutionLevel user
SetCompressor /SOLID lzma

!include "MUI2.nsh"
!include "LogicLib.nsh"
!include "x64.nsh"

!ifndef APP_ID
  !define APP_ID "DaVinciProjectManager"
!endif
!ifndef APP_LABEL
  !define APP_LABEL "DaVinci Project Manager"
!endif
!define UNINSTALL_KEY "Software\Microsoft\Windows\CurrentVersion\Uninstall\${APP_ID}"

Name "${APP_LABEL}"
OutFile "${OUTPUT}"
InstallDir "$LOCALAPPDATA\Programs\${APP_LABEL}"
ShowInstDetails show
ShowUninstDetails show
VIProductVersion "${FILE_VERSION}"
VIAddVersionKey /LANG=1033 "ProductName" "DaVinci Project Manager"
VIAddVersionKey /LANG=1033 "FileDescription" "DaVinci Project Manager Installer"
VIAddVersionKey /LANG=1033 "CompanyName" "DaVinci Project Manager contributors"
VIAddVersionKey /LANG=1033 "LegalCopyright" "Copyright (c) 2026 DaVinci Project Manager contributors"
VIAddVersionKey /LANG=1033 "FileVersion" "${VERSION}"
VIAddVersionKey /LANG=1033 "ProductVersion" "${VERSION}"

!define MUI_ICON "${APP_ICON}"
!define MUI_UNICON "${APP_ICON}"
!define MUI_ABORTWARNING
!define MUI_FINISHPAGE_RUN "$INSTDIR\DaVinciProjectManager.exe"
!define MUI_FINISHPAGE_RUN_NOTCHECKED
!define MUI_LANGDLL_REGISTRY_ROOT "HKCU"
!define MUI_LANGDLL_REGISTRY_KEY "${UNINSTALL_KEY}"
!define MUI_LANGDLL_REGISTRY_VALUENAME "InstallerLanguage"
!insertmacro MUI_PAGE_WELCOME
!insertmacro MUI_PAGE_INSTFILES
!insertmacro MUI_PAGE_FINISH
!insertmacro MUI_UNPAGE_CONFIRM
!insertmacro MUI_UNPAGE_INSTFILES
!insertmacro MUI_LANGUAGE "English"
!insertmacro MUI_LANGUAGE "Polish"

LangString WrongArchitecture ${LANG_ENGLISH} "This package requires 64-bit Windows with support for x64 applications."
LangString WrongArchitecture ${LANG_POLISH} "Pakiet wymaga 64-bitowego systemu Windows z obsługą aplikacji x64."

Function .onInit
  ${IfNot} ${RunningX64}
    MessageBox MB_ICONSTOP "$(WrongArchitecture)" /SD IDOK
    Abort
  ${EndIf}
  SetRegView 64
  SetShellVarContext current
FunctionEnd

Function un.onInit
  SetRegView 64
  SetShellVarContext current
  !insertmacro MUI_UNGETLANGUAGE
FunctionEnd

Section "DaVinci Project Manager"
  SetOutPath "$INSTDIR"
  File /r "${PAYLOAD}\*.*"
  WriteUninstaller "$INSTDIR\Uninstall.exe"
  CreateShortCut "$SMPROGRAMS\${APP_LABEL}.lnk" "$INSTDIR\DaVinciProjectManager.exe"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayName" "${APP_LABEL}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayVersion" "${VERSION}"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "Publisher" "DaVinci Project Manager contributors"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallLocation" "$INSTDIR"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "DisplayIcon" "$INSTDIR\DaVinciProjectManager.exe"
  WriteRegStr HKCU "${UNINSTALL_KEY}" "UninstallString" '"$INSTDIR\Uninstall.exe"'
  WriteRegStr HKCU "${UNINSTALL_KEY}" "QuietUninstallString" '"$INSTDIR\Uninstall.exe" /S'
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "EstimatedSize" ${ESTIMATED_KIB}
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoModify" 1
  WriteRegDWORD HKCU "${UNINSTALL_KEY}" "NoRepair" 1
  WriteRegStr HKCU "${UNINSTALL_KEY}" "InstallerLanguage" "$LANGUAGE"
SectionEnd

Section "Uninstall"
  Delete "$SMPROGRAMS\${APP_LABEL}.lnk"
  !include "${REMOVE_PAYLOAD}"
  Delete "$INSTDIR\Uninstall.exe"
  RMDir "$INSTDIR"
  DeleteRegKey HKCU "${UNINSTALL_KEY}"
SectionEnd
